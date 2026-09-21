import { clearAccount, clearAll, clearFarm, getStats } from './db';
import { useOfflineStore } from './connectivity';

/**
 * The three cache lifetimes, in one place so the wording and the scope cannot drift
 * apart from what actually gets deleted.
 *
 * All three are fire-and-forget by design: clearing a cache is housekeeping, and a
 * failure to clear must never block the user action that triggered it (signing out,
 * losing farm access). The `void` at every call site is deliberate, not an oversight.
 */

/** Sign-out: the next user of this device sees none of the previous user's farm data. */
export async function clearOfflineDataOnSignOut(): Promise<void> {
  await clearAll();
  await useOfflineStore.getState().refreshStats();
}

/** Membership to one farm is gone (or was revoked): drop that farm, keep the others. */
export async function clearOfflineDataForFarm(accountId: string, farmId: string): Promise<void> {
  await clearFarm({ accountId, farmId });
  await useOfflineStore.getState().refreshStats();
}

/** An account-level reset: used when a session is invalidated rather than signed out. */
export async function clearOfflineDataForAccount(accountId: string): Promise<void> {
  await clearAccount(accountId);
  await useOfflineStore.getState().refreshStats();
}

/** The user-facing "clear offline data" action. Returns what it removed, for the toast. */
export async function clearAllOfflineData(): Promise<{ recordCount: number; collectionCount: number }> {
  const before = await getStats();
  await clearAll();
  await useOfflineStore.getState().refreshStats();
  return { recordCount: before.recordCount, collectionCount: before.collectionCount };
}
