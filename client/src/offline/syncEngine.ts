/**
 * Sending the queue: the one place a queued mutation leaves the device.
 *
 * The algorithm is the approved one, and each rule below exists because of a specific way a
 * naive flush loses or duplicates a field worker's data:
 *
 * - **Refresh first, if the token is nearly dead.** A queue that has been sitting on a device
 *   for a week should not spend its first round trip learning the access token expired.
 * - **Oldest first, batched, sequential per farm.** A mutation id is the server's idempotency
 *   key, so a retry re-sends the *same* item and cannot duplicate it. Sequential (never
 *   parallel) because the order of a day's work is part of its meaning, and because the API's
 *   rate limiter is a fixed window that a burst would empty.
 * - **Paced, not as fast as possible.** The limiter (300 requests/minute, no queue) is
 *   partitioned by client **IP** — `UseRateLimiter()` runs before authentication, so
 *   `HttpContext.User` is unauthenticated and devices behind one rural NAT share the budget.
 *   Batching (200 items per request, the server's own transport guard) plus 500 ms between
 *   requests keeps a week-old queue at a small fraction of it.
 * - **A 429 or 5xx stops the pass and backs off with jitter.** Never a tight loop, and never
 *   \"skip the item\" — the queue is not allowed to lose work because the server was busy.
 * - **A per-item 4xx quarantines that item and continues.** One bad row must not stall a
 *   device, and the server's own message is what the user has to act on.
 * - **A pass that reached the server resets the offline-write window.** The window (5.6) exists
 *   so a device stops accepting offline work before its refresh token can expire with the queue
 *   undeliverable; a flush the server accepted is proof the opposite, and without this a device
 *   that came back online and drained its queue could still refuse the next record until some
 *   unrelated page fetch happened. Only a pass with an **authenticated answer** counts: a
 *   transport failure never reached anything, and a 401 means the session cannot deliver, which
 *   is the state the window is there to flag.
 * - **No polling.** Every trigger is an event: connectivity, focus, visibility, a successful
 *   request, a successful refresh, or an explicit \"Sync now\". A backoff schedules exactly one
 *   retry timer; nothing runs on an interval.
 */

import { getApiError } from '../api/farmApi';
import {
  ensureFreshAccessToken,
  isFarmAccessDenied,
  setSyncTriggerHandler,
} from '../api/axios';
import { isAccessTokenExpiringWithin, TOKEN_REFRESH_WINDOW_MS } from '../api/accessToken';
import { syncApi, type SyncMutationItemResult, type SyncMutationResult } from '../api/sync';
import { useAuthStore } from '../stores/authStore';
import { useOfflineStore } from './connectivity';
import { noteServerContact, resetServerContactThrottleForTests } from './offlineData';
import { useQueueStore } from './queueEvents';
import { useSyncStore } from './syncStatus';
import { getMutationKind, type MutationEnvelope } from './mutationKinds';
import {
  listPendingMutations,
  markApplied,
  markQuarantined,
  purgeOldApplied,
  recordAttempt,
} from './outbox';
import type { OfflineScope, OutboxItem } from './db';

/** Mirrors `SyncMutationRequest.MaxItemsPerRequest`; a larger batch is a 400 by design. */
export const MAX_ITEMS_PER_REQUEST = 200;

/**
 * Minimum spacing between two sync requests.
 *
 * 500 ms is 120 requests/minute — under half of the API's 300/minute per-partition window,
 * with the margin left for the app's ordinary traffic sharing it. A 10,000-item queue is 50
 * requests, about 25 seconds, and still no risk of tripping the limiter.
 */
export const BATCH_SPACING_MS = 500;

const BACKOFF_BASE_MS = 2_000;
const BACKOFF_MAX_MS = 5 * 60 * 1000;

/** A pass may not run longer than this without yielding to the event loop (test/UX hygiene). */
const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

// ── module state: one flush at a time, per app instance ─────

let inFlight: Promise<void> | null = null;
/** Something asked for a pass while one was running: try again when it finishes. */
let recheckRequested = false;
let backoffUntil = 0;
let retryTimer: ReturnType<typeof setTimeout> | null = null;
/** Consecutive failed rounds, for the exponential backoff. Reset by a clean pass. */
let failureRounds = 0;

function cancelRetryTimer(): void {
  if (retryTimer !== null) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
}

/**
 * Asks for a flush.
 *
 * Coalescing is the point: a page that fires eight requests in a second must not start eight
 * passes. While one is running, further asks set a flag and the pass runs once more at the
 * end if anything is still pending.
 */
export function requestFlush(options: { force?: boolean } = {}): Promise<void> {
  if (options.force) {
    cancelRetryTimer();
    backoffUntil = 0;
    failureRounds = 0;
  } else if (Date.now() < backoffUntil) {
    // Inside a backoff window: the scheduled retry (or a later trigger) will do it. This is
    // what stops a 429 or a 5xx from turning every page's success into another request.
    return Promise.resolve();
  }

  if (inFlight) {
    recheckRequested = true;
    return inFlight;
  }

  const force = options.force === true;

  // A trigger that arrives *while* a pass is running sets `recheckRequested`, and this loop is
  // what honours it. Handling it inside the pass would not work: `inFlight` is still assigned
  // at that point, so a nested `requestFlush()` would coalesce into the pass that is already
  // finishing and the item would wait for the next unrelated trigger.
  inFlight = (async () => {
    do {
      recheckRequested = false;
      await runPass(force);
    } while (recheckRequested && Date.now() >= backoffUntil);
  })().finally(() => {
    inFlight = null;
  });

  return inFlight;
}

async function runPass(force: boolean): Promise<void> {
  const accountId = useAuthStore.getState().user?.accountId;
  if (!accountId) return;

  // `navigator.onLine` is a hint, not a precondition (see connectivity.ts). An ordinary
  // trigger respects it; an explicit "Sync now" is the user telling us to try anyway.
  if (!force && !useOfflineStore.getState().isOnline) return;

  const sync = useSyncStore.getState();

  if (isAccessTokenExpiringWithin(TOKEN_REFRESH_WINDOW_MS)) {
    try {
      await ensureFreshAccessToken();
      sync.setSessionExpired(false);
    } catch {
      // No usable session: nothing can be sent, and saying so beats retrying against a 401.
      sync.setSessionExpired(true);
      return;
    }
  }

  const pending = await listPendingMutations(accountId);
  if (pending.length === 0) {
    // Nothing was sent, so nothing proves the server is reachable: the window is untouched.
    await finishPass({ clean: true, reachedServer: false });
    return;
  }

  sync.setFlushing(true);
  let stopped = false;
  /** Whether anything in this pass got an authenticated answer from the server. */
  let reachedServer = false;

  try {
    // Farm by farm, oldest first within each: a farm's items are contiguous in this order,
    // which is what makes the batches meaningful to the server's per-farm context.
    for (const [farmId, items] of groupByFarm(pending)) {
      const farm = await flushFarm(farmId, items);
      reachedServer = reachedServer || farm.reachedServer;
      if (farm.outcome === 'stop') {
        stopped = true;
        break;
      }
    }
  } finally {
    await finishPass({ clean: !stopped, reachedServer });
    sync.setFlushing(false);
  }
}

/** The queue grouped by farm, keeping the account-wide oldest-first order inside each group. */
function groupByFarm(items: OutboxItem[]): Map<string, OutboxItem[]> {
  const byFarm = new Map<string, OutboxItem[]>();
  for (const item of items) {
    const existing = byFarm.get(item.farmId);
    if (existing) existing.push(item);
    else byFarm.set(item.farmId, [item]);
  }
  return byFarm;
}

/**
 * Sends one farm's pending items, in batches. Returns 'stop' when the pass must end (the
 * session died, the limiter spoke, or the server is failing) and 'continue' when the next
 * farm may be tried.
 *
 * Also reports whether any request in the farm got an authenticated answer, which is what the
 * offline-write window is reset from (see the module header).
 */
async function flushFarm(
  farmId: string,
  items: OutboxItem[],
): Promise<{ outcome: 'continue' | 'stop'; reachedServer: boolean }> {
  let reachedServer = false;

  // One refresh per farm pass, then stop: "refresh once then stop" is the whole rule, and it
  // keeps a dead session from spending a request per item discovering the same thing.
  let refreshed = false;
  let offset = 0;

  while (offset < items.length) {
    const chunk = items.slice(offset, offset + MAX_ITEMS_PER_REQUEST);
    const outcome = await sendChunk(farmId, chunk);

    // Recorded before the switch so every exit below carries it, including the ones that stop
    // the pass: the server answering is the fact, whether or not the answer was welcome.
    if (reachedTheServer(outcome)) reachedServer = true;

    switch (outcome.kind) {
      case 'ok':
        await applyResults(chunk, outcome.body);
        offset += MAX_ITEMS_PER_REQUEST;
        break;

      case 'skipped':
        // Nothing in this chunk could be sent (a kind this build does not know about). The
        // items were marked as such; the pass moves on rather than treating it as a refusal.
        offset += MAX_ITEMS_PER_REQUEST;
        break;

      case 'auth': {
        if (!refreshed) {
          refreshed = true;
          try {
            await ensureFreshAccessToken();
            useSyncStore.getState().setSessionExpired(false);
            // Same chunk, same mutation ids: the retry is not a new mutation.
            continue;
          } catch {
            useSyncStore.getState().setSessionExpired(true);
            return { outcome: 'stop', reachedServer };
          }
        }

        // Already refreshed once and still refused: the session really is gone. Items stay
        // pending (not quarantined) — nothing is wrong with them, and the next session sends
        // them.
        useSyncStore.getState().setSessionExpired(true);
        return { outcome: 'stop', reachedServer };
      }

      case 'rate-limited': {
        const wait = outcome.retryAfterMs;
        scheduleRetry(wait, outcome.message);
        return { outcome: 'stop', reachedServer };
      }

      case 'retry':
        await recordAttempts(chunk, outcome.message);
        scheduleRetry(null, outcome.message);
        return { outcome: 'stop', reachedServer };

      case 'farm-denied':
        // Membership is gone for this farm. Every item in this chunk would get the same
        // answer, so each is quarantined with the server's own wording in one step — the
        // queue drains into a visible, dismissible state instead of retrying forever.
        for (const item of chunk) {
          await quarantine(item, outcome.message);
        }
        return { outcome: 'continue', reachedServer };

      case 'batch-rejected': {
        // The request itself was refused (a 400 on the batch, a 413, a proxy refusing it):
        // send the items one at a time so each gets its own verdict rather than the batch's.
        const individual = await sendIndividually(farmId, chunk);
        if (individual === 'stop') return { outcome: 'stop', reachedServer };
        offset += MAX_ITEMS_PER_REQUEST;
        break;
      }
    }

    // Pacing between requests, never after the last one: the next trigger should not pay for
    // a wait that had nothing to wait for.
    if (offset < items.length) {
      await sleep(BATCH_SPACING_MS);
    }
  }

  return { outcome: 'continue', reachedServer };
}

type ChunkOutcome =
  | { kind: 'ok'; body: SyncMutationResult }
  | { kind: 'auth' }
  | { kind: 'rate-limited'; retryAfterMs: number | null; message: string }
  /** `serverAnswered` is what separates a 5xx (the API is up and refused) from no response at
   * all (the connection went away); only the former is evidence the server is reachable. */
  | { kind: 'retry'; message: string; serverAnswered: boolean }
  | { kind: 'farm-denied'; message: string }
  | { kind: 'batch-rejected'; message: string }
  /** Nothing in the chunk was sendable by this build; the items were marked, not sent. */
  | { kind: 'skipped' };

/**
 * Whether this outcome proves the API was reached *and* the session can deliver.
 *
 * Every kind except two does: a transport failure never arrived, and a 401 means the session is
 * dead — the one state the offline-write window exists to flag, so it must not clear it.
 */
function reachedTheServer(outcome: ChunkOutcome): boolean {
  switch (outcome.kind) {
    case 'ok':
    case 'rate-limited':
    case 'farm-denied':
    case 'batch-rejected':
      return true;
    case 'retry':
      return outcome.serverAnswered;
    case 'auth':
    case 'skipped':
      return false;
  }
}

async function sendChunk(farmId: string, chunk: OutboxItem[]): Promise<ChunkOutcome> {
  const envelopes: MutationEnvelope[] = [];
  const unsendable: OutboxItem[] = [];

  for (const item of chunk) {
    const definition = getMutationKind(item.kind);
    if (definition) envelopes.push(definition.toEnvelope(item));
    // A kind this build does not know means the device is running an older bundle than the
    // one that queued the item (a cached shell after a deploy). Left pending on purpose: it
    // is not the user's mistake, and a newer bundle can still send it.
    else unsendable.push(item);
  }

  for (const item of unsendable) {
    await recordAttempt(
      scopeOf(item),
      item.mutationId,
      'This app version does not know how to send this record. Update the app.',
    );
  }

  if (envelopes.length === 0) {
    // Never quarantined and never sent: an item this build cannot send is not the user's
    // mistake, and a newer bundle can still deliver it.
    return { kind: 'skipped' };
  }

  try {
    const response = await syncApi.applyMutations(farmId, envelopes);
    return { kind: 'ok', body: response.data };
  } catch (error) {
    const status = readStatus(error);

    if (status === 401) return { kind: 'auth' };

    if (status === 429) {
      return {
        kind: 'rate-limited',
        retryAfterMs: readRetryAfterMs(error),
        message: 'The server is busy. Waiting before trying again.',
      };
    }

    if (status !== undefined && status >= 500) {
      return {
        kind: 'retry',
        message: getApiError(error, 'The server could not save this yet'),
        serverAnswered: true,
      };
    }

    if (isFarmAccessDenied(error)) {
      return { kind: 'farm-denied', message: getApiError(error, 'Access denied to this farm') };
    }

    if (status !== undefined) {
      return { kind: 'batch-rejected', message: getApiError(error, 'This batch was refused') };
    }

    // No response at all: the connection went away mid-flush. Retry later, change nothing —
    // and do not claim the server was reached, because it was not.
    return { kind: 'retry', message: getApiError(error, 'No connection'), serverAnswered: false };
  }
}

/**
 * Sends a refused batch one item at a time, so each item gets the verdict the batch could not
 * give it. Stops the same way a batch does (session, limiter, server failure).
 */
async function sendIndividually(
  farmId: string,
  chunk: OutboxItem[],
): Promise<'continue' | 'stop'> {
  for (let index = 0; index < chunk.length; index++) {
    const item = chunk[index];
    const outcome = await sendChunk(farmId, [item]);

    switch (outcome.kind) {
      case 'ok':
        await applyResults([item], outcome.body);
        break;
      case 'skipped':
        break;
      case 'auth':
        useSyncStore.getState().setSessionExpired(true);
        return 'stop';
      case 'rate-limited':
        scheduleRetry(outcome.retryAfterMs, outcome.message);
        return 'stop';
      case 'retry':
        await recordAttempts([item], outcome.message);
        scheduleRetry(null, outcome.message);
        return 'stop';
      case 'farm-denied': {
        // The same answer for every remaining item in this farm's chunk: take it once.
        for (const remaining of chunk.slice(index)) {
          await quarantine(remaining, outcome.message);
        }
        return 'continue';
      }
      case 'batch-rejected':
        // A single item refused as a request: the server's body is this item's message.
        await quarantine(item, outcome.message);
        break;
    }

    if (index + 1 < chunk.length) await sleep(BATCH_SPACING_MS);
  }

  return 'continue';
}

/** Maps the batch's per-item results onto the queue. */
async function applyResults(chunk: OutboxItem[], body: SyncMutationResult): Promise<void> {
  const results = new Map<string, SyncMutationItemResult>();
  for (const item of body.items ?? []) {
    results.set(item.clientMutationId.toLowerCase(), item);
  }

  for (const item of chunk) {
    // Unknown kinds never reached the server, so they are not in the answer.
    if (!getMutationKind(item.kind)) continue;

    const result = results.get(item.mutationId.toLowerCase());
    if (!result) {
      await recordAttempt(scopeOf(item), item.mutationId, 'The server did not report a result for this record');
      continue;
    }

    if (result.outcome === 'Rejected') {
      await quarantine(item, result.message ?? 'The server refused this record');
      continue;
    }

    await markApplied(scopeOf(item), item.mutationId, {
      entityId: result.targetEntityId,
      // Superseded wrote nothing (the intent was already satisfied) and the server's wording
      // explains that; showing it beats an unexplained row.
      message: result.outcome === 'Superseded' ? result.message : null,
    });
  }
}

async function recordAttempts(chunk: OutboxItem[], message: string): Promise<void> {
  for (const item of chunk) {
    await recordAttempt(scopeOf(item), item.mutationId, message);
  }
}

async function quarantine(item: OutboxItem, message: string): Promise<void> {
  await markQuarantined(scopeOf(item), item.mutationId, message);
}

/**
 * Ends a pass: reaps old applied items, refreshes the counts every queue view reads, and
 * clears or arms the backoff.
 *
 * A pass the server answered also resets the offline-write window. Called directly rather than
 * through the trigger chokepoint on purpose: queue traffic is marked `syncRequest`, which is
 * what stops a flush from re-triggering itself, and that must stay true.
 */
async function finishPass(options: { clean: boolean; reachedServer: boolean }): Promise<void> {
  const sync = useSyncStore.getState();

  await purgeOldApplied();

  if (options.reachedServer) {
    try {
      await noteServerContact();
    } catch {
      // A storage failure must not fail the pass that proved the connection works.
    }
  }

  if (options.clean) {
    failureRounds = 0;
    backoffUntil = 0;
    cancelRetryTimer();
    sync.setError(null);
    sync.setLastFlushAt(new Date().toISOString());
  }

  await sync.refresh();
}

/**
 * Arms exactly one retry.
 *
 * `retryAfterMs` is the server's own answer when it gave one (a `Retry-After` header, else the
 * body's `retryAfter`); otherwise the exponential ladder, jittered *downwards* so the cap is
 * a real ceiling. One timer, cleared by the next clean pass — never an interval.
 */
function scheduleRetry(retryAfterMs: number | null, message: string): void {
  failureRounds += 1;
  const ladder = Math.min(BACKOFF_MAX_MS, BACKOFF_BASE_MS * 2 ** (failureRounds - 1));
  const delay = retryAfterMs !== null && retryAfterMs > 0
    ? Math.min(BACKOFF_MAX_MS, retryAfterMs)
    : Math.max(500, Math.round(ladder * (0.7 + Math.random() * 0.3)));

  backoffUntil = Date.now() + delay;
  useSyncStore.getState().setError(`${message} Retrying in ${Math.max(1, Math.round(delay / 1000))}s.`);

  cancelRetryTimer();
  retryTimer = setTimeout(() => {
    retryTimer = null;
    void requestFlush();
  }, delay);
}

function scopeOf(item: OutboxItem): OfflineScope {
  return { accountId: item.accountId, farmId: item.farmId };
}

function readStatus(error: unknown): number | undefined {
  return (error as { response?: { status?: number } })?.response?.status;
}

/** `Retry-After` header if the server sent one, else the limiter's body field. */
function readRetryAfterMs(error: unknown): number | null {
  const response = (error as {
    response?: { headers?: Record<string, unknown>; data?: unknown };
  })?.response;

  const header = response?.headers?.['retry-after'] ?? response?.headers?.['Retry-After'];
  if (typeof header === 'string' && header.trim() !== '') {
    const seconds = Number(header);
    if (Number.isFinite(seconds) && seconds >= 0) return seconds * 1000;
    const asDate = Date.parse(header);
    if (!Number.isNaN(asDate)) return Math.max(0, asDate - Date.now());
  }

  const body = response?.data as { retryAfter?: unknown } | undefined;
  if (typeof body?.retryAfter === 'number' && Number.isFinite(body.retryAfter) && body.retryAfter >= 0) {
    return body.retryAfter * 1000;
  }

  return null;
}

// ── triggers ────────────────────────────────────────────────

/**
 * Wires the flush to the events that make it possible, and runs one pass at boot.
 *
 * Deliberately no interval. The events cover every way the app learns there may be a
 * connection now: the browser's own online event after a suspend, the window regaining focus,
 * any request succeeding (the cheapest possible reachability proof), a token refresh, and the
 * user's "Sync now".
 */
export function initSyncEngine(): () => void {
  if (typeof window === 'undefined' || typeof window.addEventListener !== 'function') {
    return () => undefined;
  }

  setSyncTriggerHandler(() => {
    // Every reason this fires — an ordinary request succeeding, or the token being refreshed —
    // means the API was just reachable, which is exactly what the offline-write window measures
    // (5.6). Recorded here, at the one chokepoint, so no page has to remember to say so.
    void noteServerContact();
    void requestFlush();
  });

  const onOnline = () => {
    void requestFlush();
  };
  const onFocus = () => {
    void requestFlush();
  };
  const onVisibility = () => {
    if (document.visibilityState === 'visible') void requestFlush();
  };

  window.addEventListener('online', onOnline);
  window.addEventListener('focus', onFocus);
  document.addEventListener('visibilitychange', onVisibility);

  // The badge follows the queue: every write announces itself, and this is the one place that
  // turns that into a count.
  const unsubscribe = useQueueStore.subscribe(() => {
    void useSyncStore.getState().refresh();
  });

  // A queue that is already there when the app starts. Nothing awaits it: boot must not block
  // on the network, and a failed pass just arms its own retry.
  void requestFlush();

  return () => {
    window.removeEventListener('online', onOnline);
    window.removeEventListener('focus', onFocus);
    document.removeEventListener('visibilitychange', onVisibility);
    unsubscribe();
    setSyncTriggerHandler(null);
    cancelRetryTimer();
  };
}

/** Test seam: forget the pass state so one test's backoff cannot leak into the next. */
export function resetSyncEngineForTests(): void {
  cancelRetryTimer();
  inFlight = null;
  recheckRequested = false;
  backoffUntil = 0;
  failureRounds = 0;
  resetServerContactThrottleForTests();
}
