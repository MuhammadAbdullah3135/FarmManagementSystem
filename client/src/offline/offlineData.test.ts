import { beforeEach, describe, expect, it } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import {
  clearAllOfflineData,
  clearOfflineDataForAccount,
  clearOfflineDataForFarm,
  clearOfflineDataOnSignOut,
} from './offlineData';
import { getCollection, getMeta, getStats, listOutboxForScope, putOutboxItem, resetOfflineDbConnection, replaceCollection, touchSessionMarker, getSessionMarker, type OutboxItem } from './db';
import { useOfflineStore } from './connectivity';
import { useAuthStore } from '../stores/authStore';

const farmA = { accountId: 'acct-1', farmId: 'farm-a' };
const farmB = { accountId: 'acct-1', farmId: 'farm-b' };
const otherAccount = { accountId: 'acct-2', farmId: 'farm-a' };

const queued = (scope: { accountId: string; farmId: string }, mutationId: string): OutboxItem => ({
  ...scope,
  mutationId,
  kind: 'weight.record',
  targetId: 'a-1',
  occurredAt: '2026-09-23T06:00:00.000Z',
  queuedAt: '2026-09-23T06:00:00.000Z',
  payload: { animalId: 'a-1', weightKg: 412, recordedAt: '2026-09-23T06:00:00.000Z' },
  status: 'pending',
  attempts: 0,
  lastError: null,
  serverMessage: null,
  appliedAt: null,
  appliedEntityId: null,
  dismissedReason: null,
});

describe('offline data lifetime', () => {
  beforeEach(async () => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });

    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: { title: 'A' } }]);
    await replaceCollection(farmB, 'tasks', [{ id: 't-2', data: { title: 'B' } }]);
    await replaceCollection(otherAccount, 'animals', [{ id: 'a-1', data: {} }]);
  });

  it('empties the read cache on sign-out, so the next user sees nothing', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));

    await clearOfflineDataOnSignOut();

    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
    expect(useOfflineStore.getState().stats.recordCount).toBe(0);
    // The queue is not the previous user's data to destroy: it is the only copy of a
    // measurement, and the next session for that account can still deliver it.
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
  });

  it('drops only the read cache of the farm whose access was lost', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));

    await clearOfflineDataForFarm(farmA.accountId, farmA.farmId);

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getCollection(farmB, 'tasks')).toHaveLength(1);
    expect(await getCollection(otherAccount, 'animals')).toHaveLength(1);

    // The queue survives: the flush will quarantine these with the server's own refusal,
    // which is how the user finds out they cannot be sent.
    expect(await listOutboxForScope(farmA)).toHaveLength(1);

    // The banner's numbers must follow what was actually removed.
    expect(useOfflineStore.getState().stats.recordCount).toBe(2);
  });

  /**
   * Delta-synced content is cleared the same way everything else is (5.6).
   *
   * The failure this guards against is subtle: a cursor lives in the collection's metadata, not
   * in its rows, so clearing "the rows" while keeping the cursor would leave a device that has
   * lost access to a farm holding a cursor for it — and the next delta read would be answered
   * from a position it no longer has any right to.
   */
  it('clears a delta-synced collection’s rows and its cursor, and only for the farm that lost access', async () => {
    await replaceCollection(farmA, 'tasks@firstPage', [{ id: 't-1', data: { title: 'A' } }], {
      cursor: 'cursor-a',
    });
    await replaceCollection(farmB, 'tasks@firstPage', [{ id: 't-2', data: { title: 'B' } }], {
      cursor: 'cursor-b',
    });

    await clearOfflineDataForFarm(farmA.accountId, farmA.farmId);

    expect(await getMeta(farmA, 'tasks@firstPage')).toBeNull();
    expect(await getCollection(farmA, 'tasks@firstPage')).toHaveLength(0);

    // The other farm's copy — rows and cursor — is untouched.
    expect((await getMeta(farmB, 'tasks@firstPage'))?.cursor).toBe('cursor-b');
    expect(await getCollection(farmB, 'tasks@firstPage')).toHaveLength(1);
  });

  it('drops only the account that was reset', async () => {
    await clearOfflineDataForAccount('acct-1');

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getCollection(farmB, 'tasks')).toHaveLength(0);
    expect(await getCollection(otherAccount, 'animals')).toHaveLength(1);
  });

  it('reports what the user-facing clear removed, including the unsynced records it discards', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));
    await putOutboxItem(queued(farmA, 'm-2'));
    useAuthStore.setState({
      user: {
        userId: 'user-1',
        accountId: 'acct-1',
        email: 'owner@example.com',
        firstName: 'Owner',
        lastName: 'Person',
        roles: ['FarmManager'],
      },
      isAuthenticated: true,
    });

    const cleared = await clearAllOfflineData();

    expect(cleared).toEqual({ recordCount: 3, collectionCount: 3, queuedCount: 2 });
    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
    // The one action that empties the queue — and it told the caller what it took.
    expect(await listOutboxForScope(farmA)).toHaveLength(0);
  });

  it('keeps the session marker when the user clears offline data', async () => {
    await touchSessionMarker(farmA.accountId, '2026-09-23T08:00:00.000Z');

    await clearAllOfflineData();

    // The marker is not data the user owns, it is a fact about the session — and if clearing
    // reset it, clearing would be the way to keep writing offline past the window that stops a
    // device from recording records it can no longer deliver.
    expect((await getSessionMarker(farmA.accountId))?.lastServerContactAt)
      .toBe('2026-09-23T08:00:00.000Z');
  });
});
