import { beforeEach, describe, expect, it } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import {
  clearAllOfflineData,
  clearOfflineDataForAccount,
  clearOfflineDataForFarm,
  clearOfflineDataOnSignOut,
} from './offlineData';
import { getCollection, getStats, replaceCollection, resetOfflineDbConnection } from './db';
import { useOfflineStore } from './connectivity';

const farmA = { accountId: 'acct-1', farmId: 'farm-a' };
const farmB = { accountId: 'acct-1', farmId: 'farm-b' };
const otherAccount = { accountId: 'acct-2', farmId: 'farm-a' };

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

  it('empties the device on sign-out, so the next user sees nothing', async () => {
    await clearOfflineDataOnSignOut();

    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
    expect(useOfflineStore.getState().stats.recordCount).toBe(0);
  });

  it('drops only the farm whose access was lost', async () => {
    await clearOfflineDataForFarm(farmA.accountId, farmA.farmId);

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getCollection(farmB, 'tasks')).toHaveLength(1);
    expect(await getCollection(otherAccount, 'animals')).toHaveLength(1);

    // The banner's numbers must follow what was actually removed.
    expect(useOfflineStore.getState().stats.recordCount).toBe(2);
  });

  it('drops only the account that was reset', async () => {
    await clearOfflineDataForAccount('acct-1');

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getCollection(farmB, 'tasks')).toHaveLength(0);
    expect(await getCollection(otherAccount, 'animals')).toHaveLength(1);
  });

  it('reports what the user-facing clear removed', async () => {
    const cleared = await clearAllOfflineData();

    expect(cleared).toEqual({ recordCount: 3, collectionCount: 3 });
    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
  });
});
