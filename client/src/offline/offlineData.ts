import {
  clearAccount,
  clearCachedData,
  clearFarm,
  clearQueue,
  getOutboxCounts,
  getSessionMarker,
  getStats,
  touchSessionMarker,
} from './db';
import { sessionStateFrom, type OfflineSessionState } from './offlinePolicy';
import { useOfflineStore } from './connectivity';
import { useQueueStore } from './queueEvents';
import { useSyncStore } from './syncStatus';
import { useAuthStore } from '../stores/authStore';

/**
 * The cache lifetimes, in one place so the wording and the scope cannot drift apart from what
 * actually gets deleted.
 *
 * There are now two lifetimes, and they are different on purpose:
 *
 * - **The read cache is disposable.** The API can hand it back at any time, so losing it costs
 *   a round trip. It goes on sign-out, on losing a farm, and on an account reset.
 * - **The write queue is not.** A queued item is the only copy of a measurement until the
 *   server has accepted it, so it survives sign-out (the next session can still deliver it, and
 *   the server re-checks farm membership at sync time) and survives losing a farm (where the
 *   flush will quarantine it with the server's own message rather than dropping it). Only the
 *   user-facing "clear offline data" action deletes queue rows, and its confirmation names how
 *   many unsynced records that discards.
 *
 * All of these are fire-and-forget by design: clearing a cache is housekeeping, and a failure
 * to clear must never block the user action that triggered it.
 */

// ── the session's offline-write window ──────────────────────

/**
 * How close this session is to the point where it can no longer deliver what the device holds.
 *
 * Read from the marker rather than from the access token: the token is refreshed every fifteen
 * minutes while online and expires while offline, so its own expiry says nothing about whether
 * *this device* has been able to reach the API. `accountId` is only passed by callers that are
 * not acting for the signed-in user (the engine, a test).
 */
export async function currentSessionState(accountId?: string): Promise<OfflineSessionState> {
  const id = accountId ?? useAuthStore.getState().user?.accountId;
  if (!id) return sessionStateFrom(null);

  const marker = await getSessionMarker(id);
  return sessionStateFrom(marker?.lastServerContactAt ?? null);
}

/**
 * Records that this account has just reached the API.
 *
 * Called from the one chokepoint every successful request goes through, so the clock behind the
 * offline-write window is "when was the API last actually reachable" rather than anything the
 * user or a page has to remember to say. Fire-and-forget: a storage failure here must never
 * fail the request that proved the connection works.
 */
export async function noteServerContact(
  at: string = new Date().toISOString(),
  accountId?: string,
): Promise<void> {
  const id = accountId ?? useAuthStore.getState().user?.accountId;
  if (!id) return;

  // A page fires several requests at once, and every one of them is "we reached the server".
  // Writing the same fact once a minute costs nothing and says the same thing: the window this
  // feeds is measured in days, so half a minute of slack cannot matter to it. Without this,
  // every request in a burst would be its own write.
  const now = Date.now();
  if (lastServerContactWrite && lastServerContactWrite.accountId === id
    && now - lastServerContactWrite.at < 30_000) {
    return;
  }

  lastServerContactWrite = { accountId: id, at: now };
  await touchSessionMarker(id, at);
}

/** In-memory throttle state for `noteServerContact`; not persisted, and not authoritative. */
let lastServerContactWrite: { accountId: string; at: number } | null = null;

/**
 * Test seam: forget the throttle, so a test that wants to observe a marker write is not
 * silently swallowed by a write another test made thirty seconds of fake time ago.
 */
export function resetServerContactThrottleForTests(): void {
  lastServerContactWrite = null;
}

/** Sign-out: the next user of this device sees none of the previous user's cached farm data. */
export async function clearOfflineDataOnSignOut(): Promise<void> {
  await clearCachedData();
  await useOfflineStore.getState().refreshStats();
}

/**
 * Membership to one farm is gone (or was revoked): drop that farm's cache, keep the others.
 *
 * Its queued items are deliberately kept. The server answers them with the farm-context 403,
 * which the flush turns into a quarantine carrying the server's message — the user sees why the
 * records cannot be sent instead of finding them silently missing.
 */
export async function clearOfflineDataForFarm(accountId: string, farmId: string): Promise<void> {
  await clearFarm({ accountId, farmId });
  await useOfflineStore.getState().refreshStats();
}

/** An account-level reset: used when a session is invalidated rather than signed out. */
export async function clearOfflineDataForAccount(accountId: string): Promise<void> {
  await clearAccount(accountId);
  await useOfflineStore.getState().refreshStats();
}

export interface ClearOfflineDataResult {
  /** Cached API rows that were removed. */
  recordCount: number;
  collectionCount: number;
  /** Queued mutations that had never reached the server and are now gone. */
  queuedCount: number;
}

/**
 * The user-facing "clear offline data" action: the one thing that empties the queue as well as
 * the cache. Returns what it removed so the caller can say so — including the count of unsynced
 * records, which is the number that matters when someone taps this by accident.
 */
export async function clearAllOfflineData(): Promise<ClearOfflineDataResult> {
  const before = await getStats();
  const accountId = useAuthStore.getState().user?.accountId;
  // Counted before the clear, because afterwards there is nothing left to count — and this
  // number is what the caller's confirmation names.
  const queuedCount = accountId ? (await getOutboxCounts(accountId)).pending : 0;

  await clearCachedData();
  await clearQueue();

  await useOfflineStore.getState().refreshStats();
  useQueueStore.getState().noteQueueChanged();
  await useSyncStore.getState().refresh();

  return {
    recordCount: before.recordCount,
    collectionCount: before.collectionCount,
    queuedCount,
  };
}
