/**
 * The write queue's typed layer — the only module above `db.ts` that touches the outbox.
 *
 * The queue is where a change made with no connection lives until the server has accepted
 * it, so three things are true of everything below and are not negotiable:
 *
 * 1. **Identity is written once.** `mutationId`, `kind`, `targetId`, `occurredAt`, `queuedAt`
 *    and `payload` are set by `enqueueMutation` and are unreachable afterwards: every writer
 *    here goes through `db.updateOutboxItem`, whose callback can only return the outcome
 *    half. A retry re-sends the *same* mutation id, which is what makes the server's
 *    idempotency guarantee bind; a rewritten id would silently duplicate work.
 * 2. **Nothing is dropped.** A refusal keeps the item with the server's own message
 *    (`markQuarantined`); giving up on one requires an explicit reason (`markDismissed`) and
 *    still keeps the record. Only the user-facing "clear offline data" action deletes queue
 *    rows, and it warns first.
 * 3. **Every key carries account and farm.** Reads take an explicit scope, so one farm's
 *    queue cannot be listed, flushed or counted as another's even in principle.
 */

import {
  deleteOutboxItem,
  getOutboxCounts,
  listOutboxByStatus,
  listOutboxForAccount,
  listOutboxForScope,
  purgeAppliedBefore,
  putOutboxItem,
  updateOutboxItem,
  type OfflineScope,
  type OutboxCounts,
  type OutboxItem,
  type OutboxStatus,
} from './db';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { useQueueStore } from './queueEvents';
import { currentSessionState } from './offlineData';
import {
  queueCapacityFrom,
  queueFullMessage,
  queueWarningMessage,
  sessionBlockedMessage,
  type OfflineSessionState,
} from './offlinePolicy';

export type { OutboxCounts, OutboxItem, OutboxStatus };

/** How long an applied item is kept before it is reaped, so the UI can show what just went. */
export const APPLIED_RETENTION_MS = 24 * 60 * 60 * 1000;

/** The account and farm a new mutation belongs to, from the signed-in session. */
export function activeScope(): OfflineScope | null {
  const accountId = useAuthStore.getState().user?.accountId;
  const farmId = useFarmStore.getState().activeFarm?.id;
  return accountId && farmId ? { accountId, farmId } : null;
}

export interface EnqueueInput<TPayload> {
  scope: OfflineScope;
  kind: string;
  targetId: string;
  /** When the change happened on the device, as an ISO string. */
  occurredAt: string;
  payload: TPayload;
  /** Injectable for tests; a v4 UUID otherwise. */
  mutationId?: string;
}

/**
 * A v4 id for a queued mutation.
 *
 * `crypto.randomUUID` needs a secure context, which the deployed app and the WebView both
 * are — but the local dev server over plain http is not, and a queue that cannot mint an id
 * cannot record anything. The fallbacks keep that device working rather than failing on a
 * capability the id does not actually need (uniqueness, not unpredictability).
 */
export function newMutationId(): string {
  const cryptoObj = globalThis.crypto as Crypto | undefined;

  if (typeof cryptoObj?.randomUUID === 'function') {
    return cryptoObj.randomUUID();
  }

  const bytes = new Uint8Array(16);
  if (typeof cryptoObj?.getRandomValues === 'function') {
    cryptoObj.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  }

  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Announces a completed write so every queue view re-reads, then passes the result through.
 *
 * Every writer below funnels through here, so "the badge follows the queue" is a property of
 * this module rather than of each call site remembering to say so.
 */
async function announce<T extends OutboxItem | null>(result: T): Promise<T> {
  if (result) useQueueStore.getState().noteQueueChanged();
  return result;
}

export type EnqueueRefusalReason = 'storage-unavailable' | 'session-stale' | 'queue-full';

/**
 * What became of an attempt to queue something.
 *
 * A refusal is deliberately not the same shape as an error: the item was not stored, and the
 * caller owes the user the *reason* — "this device is full" and "this session is too old to
 * deliver it" are different problems with different ways out, and neither is "storage is
 * broken". `warning` is the third case: it worked, and there is something to say about it
 * before the queue is full rather than when it is.
 */
export interface EnqueueResult<TPayload> {
  item: OutboxItem<TPayload> | null;
  refusal: EnqueueRefusalReason | null;
  /** The refusal in the user's words, or null when the item was queued. */
  message: string | null;
  /** Something to say about a *successful* enqueue, or null. */
  warning: string | null;
}

/**
 * Queues one mutation, or explains why it cannot be queued.
 *
 * This is the only writer on the queue, which is why both of 5.6's limits are enforced here:
 * a page cannot forget to ask whether the session is still young enough to deliver what it is
 * about to store, and a fourth workflow added later inherits the check by construction rather
 * than by reading the architecture note.
 */
export async function enqueueMutation<TPayload>(
  input: EnqueueInput<TPayload>,
  options: {
    /** The session's state, so the check happens in one place. */
    sessionState?: OfflineSessionState;
    /** Injectable for tests. */
    pendingCount?: number;
  } = {},
): Promise<EnqueueResult<TPayload>> {
  // Both limits are read before the item is built, and in parallel: they are two independent
  // storage reads, and a device that is out of signal should not pay for them one after the
  // other on the way to saving a measurement.
  const [sessionState, pendingCount] = await Promise.all([
    options.sessionState ? Promise.resolve(options.sessionState) : currentSessionState(),
    options.pendingCount !== undefined
      ? Promise.resolve(options.pendingCount)
      : getCounts(input.scope.accountId).then((counts) => counts.pending),
  ]);

  if (!sessionState.writeAllowed) {
    return {
      item: null,
      refusal: 'session-stale',
      message: sessionBlockedMessage(sessionState.daysSinceServerContact ?? 0),
      warning: null,
    };
  }

  // Counted from the store, not from the caller's view: the badge and a page's own pending
  // rows are both partial pictures of one account-wide queue.
  const capacity = queueCapacityFrom(pendingCount);

  if (capacity.full) {
    return { item: null, refusal: 'queue-full', message: queueFullMessage(), warning: null };
  }

  const item: OutboxItem<TPayload> = {
    accountId: input.scope.accountId,
    farmId: input.scope.farmId,
    mutationId: input.mutationId ?? newMutationId(),
    kind: input.kind,
    targetId: input.targetId,
    occurredAt: input.occurredAt,
    queuedAt: new Date().toISOString(),
    payload: input.payload,
    status: 'pending',
    attempts: 0,
    lastError: null,
    serverMessage: null,
    appliedAt: null,
    appliedEntityId: null,
    dismissedReason: null,
  };

  const stored = await putOutboxItem(item);

  if (!stored) {
    return {
      item: null,
      refusal: 'storage-unavailable',
      message: 'This device has no usable offline storage, so the record could not be saved.',
      warning: null,
    };
  }

  return {
    item: await announce(item),
    refusal: null,
    message: null,
    warning: capacity.warning ? queueWarningMessage(capacity.remaining) : null,
  };
}

// ── outcome writers: the only mutations that exist after enqueue ──

/** The outcome half as it stands, so each writer changes only what it owns. */
function outcomeOf(item: OutboxItem) {
  return {
    status: item.status,
    attempts: item.attempts,
    lastError: item.lastError,
    serverMessage: item.serverMessage,
    appliedAt: item.appliedAt,
    appliedEntityId: item.appliedEntityId,
    dismissedReason: item.dismissedReason,
  };
}

/** A failed attempt that wrote nothing: the item stays pending and its count goes up. */
export async function recordAttempt(
  scope: OfflineScope,
  mutationId: string,
  message: string,
): Promise<OutboxItem | null> {
  return announce(await updateOutboxItem(scope, mutationId, (item) => ({
    ...outcomeOf(item),
    status: 'pending',
    attempts: item.attempts + 1,
    lastError: message,
  })));
}

/**
 * The server accepted the item (or reported its intent already satisfied).
 *
 * `message` carries the server's text for the superseded case, where nothing was written but
 * the device may treat the item as done — the sync screen shows it rather than pretending
 * the row was created.
 */
export async function markApplied(
  scope: OfflineScope,
  mutationId: string,
  options: { entityId?: string | null; message?: string | null; appliedAt?: string } = {},
): Promise<OutboxItem | null> {
  return announce(await updateOutboxItem(scope, mutationId, (item) => ({
    ...outcomeOf(item),
    status: 'applied',
    appliedAt: options.appliedAt ?? new Date().toISOString(),
    appliedEntityId: options.entityId ?? null,
    serverMessage: options.message ?? null,
    lastError: null,
  })));
}

/** The server refused it: quarantined, with the server's own wording preserved verbatim. */
export async function markQuarantined(
  scope: OfflineScope,
  mutationId: string,
  serverMessage: string,
): Promise<OutboxItem | null> {
  return announce(await updateOutboxItem(scope, mutationId, (item) => ({
    ...outcomeOf(item),
    status: 'quarantined',
    attempts: item.attempts + 1,
    serverMessage,
    lastError: null,
  })));
}

/** The user gave up on a quarantined item, stating why. The record is kept, not deleted. */
export async function markDismissed(
  scope: OfflineScope,
  mutationId: string,
  reason: string,
): Promise<OutboxItem | null> {
  return announce(await updateOutboxItem(scope, mutationId, (item) => ({
    ...outcomeOf(item),
    status: 'dismissed',
    dismissedReason: reason,
  })));
}

/** Puts a quarantined or dismissed item back in the queue, to be tried again. */
export async function requeueMutation(
  scope: OfflineScope,
  mutationId: string,
): Promise<OutboxItem | null> {
  return announce(await updateOutboxItem(scope, mutationId, (item) => ({
    ...outcomeOf(item),
    status: 'pending',
    // The previous refusal is history once the user asks for another attempt; keeping it
    // on screen next to a pending item would describe a state that no longer exists.
    serverMessage: null,
    dismissedReason: null,
    lastError: null,
  })));
}

// ── reads ───────────────────────────────────────────────────

/** Everything pending for this account, across every farm, oldest first. The flush's input. */
export async function listPendingMutations<TPayload = unknown>(
  accountId: string,
): Promise<OutboxItem<TPayload>[]> {
  return listOutboxByStatus<TPayload>(accountId, 'pending');
}

/** One farm's whole queue, oldest first — what the sync screen shows. */
export async function listScopeMutations<TPayload = unknown>(
  scope: OfflineScope,
): Promise<OutboxItem<TPayload>[]> {
  return listOutboxForScope<TPayload>(scope);
}

export async function listAccountMutations<TPayload = unknown>(
  accountId: string,
): Promise<OutboxItem<TPayload>[]> {
  return listOutboxForAccount<TPayload>(accountId);
}

export async function getCounts(accountId: string): Promise<OutboxCounts> {
  return getOutboxCounts(accountId);
}

export async function removeMutation(scope: OfflineScope, mutationId: string): Promise<void> {
  await deleteOutboxItem(scope, mutationId);
  useQueueStore.getState().noteQueueChanged();
}

/** Reaps applied items from before the retention window. Returns how many went. */
export async function purgeOldApplied(
  now: number = Date.now(),
): Promise<number> {
  return purgeAppliedBefore(new Date(now - APPLIED_RETENTION_MS).toISOString());
}
