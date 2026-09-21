import { beforeEach, describe, expect, it } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import {
  clearAccount,
  clearAll,
  clearFarm,
  deleteCollection,
  getCollection,
  getMeta,
  getRecord,
  getScopeMeta,
  getStats,
  isOfflineStorageAvailable,
  putRecords,
  replaceCollection,
  resetOfflineDbConnection,
  type CachedRecord,
} from './db';

const farmA = { accountId: 'acct-1', farmId: 'farm-a' };
const farmB = { accountId: 'acct-1', farmId: 'farm-b' };
const otherAccount = { accountId: 'acct-2', farmId: 'farm-a' };

const record = (
  scope: { accountId: string; farmId: string },
  collection: string,
  id: string,
  data: unknown,
): CachedRecord => ({ ...scope, collection, id, data, fetchedAt: '2026-09-20T06:00:00.000Z' });

describe('offline store', () => {
  beforeEach(() => {
    // A fresh factory per test: this is the same "new database" the app gets on a
    // wiped device, without needing to delete anything between tests.
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
  });

  it('round-trips a cached record with its fetch time', async () => {
    await putRecords([record(farmA, 'animals', 'a-1', { tagNumber: 'C-001' })]);

    const stored = await getRecord<{ tagNumber: string }>(farmA, 'animals', 'a-1');

    expect(stored).not.toBeNull();
    expect(stored!.data.tagNumber).toBe('C-001');
    expect(stored!.fetchedAt).toBe('2026-09-20T06:00:00.000Z');
  });

  it('keeps collections apart inside one farm', async () => {
    await putRecords([
      record(farmA, 'animals', 'a-1', { tagNumber: 'C-001' }),
      record(farmA, 'tasks', 'a-1', { title: 'Clean barn' }),
    ]);

    const animals = await getCollection(farmA, 'animals');
    const tasks = await getCollection(farmA, 'tasks');

    expect(animals).toHaveLength(1);
    expect(tasks).toHaveLength(1);
    expect((animals[0].data as { tagNumber: string }).tagNumber).toBe('C-001');
    expect((tasks[0].data as { title: string }).title).toBe('Clean barn');
  });

  /// The same id in two farms must never resolve to the same row: farm scoping is what
  /// makes it impossible to serve one farm's data while another is active.
  it('keeps the same id apart across farms and accounts', async () => {
    await putRecords([
      record(farmA, 'animals', 'shared-id', { tagNumber: 'FARM-A' }),
      record(farmB, 'animals', 'shared-id', { tagNumber: 'FARM-B' }),
      record(otherAccount, 'animals', 'shared-id', { tagNumber: 'OTHER-ACCOUNT' }),
    ]);

    expect((await getRecord<{ tagNumber: string }>(farmA, 'animals', 'shared-id'))!.data.tagNumber).toBe('FARM-A');
    expect((await getRecord<{ tagNumber: string }>(farmB, 'animals', 'shared-id'))!.data.tagNumber).toBe('FARM-B');
    expect((await getRecord<{ tagNumber: string }>(otherAccount, 'animals', 'shared-id'))!.data.tagNumber)
      .toBe('OTHER-ACCOUNT');

    expect(await getCollection(farmA, 'animals')).toHaveLength(1);
    expect(await getCollection(farmB, 'animals')).toHaveLength(1);
  });

  it('replaces a whole collection and records what it holds', async () => {
    await replaceCollection(farmA, 'tasks', [
      { id: 't-1', data: { title: 'One' } },
      { id: 't-2', data: { title: 'Two' } },
    ], { fetchedAt: '2026-09-20T07:00:00.000Z' });

    const meta = await getMeta(farmA, 'tasks');
    expect(meta).not.toBeNull();
    expect(meta!.recordCount).toBe(2);
    expect(meta!.lastSyncedAt).toBe('2026-09-20T07:00:00.000Z');

    // A second refresh replaces rather than accumulates: the row that is gone from the
    // server must be gone from the cache, or the UI would keep offering a deleted task.
    await replaceCollection(farmA, 'tasks', [
      { id: 't-2', data: { title: 'Two, renamed' } },
    ], { fetchedAt: '2026-09-21T07:00:00.000Z' });

    const rows = await getCollection(farmA, 'tasks');
    expect(rows).toHaveLength(1);
    expect((rows[0].data as { title: string }).title).toBe('Two, renamed');
    expect((await getMeta(farmA, 'tasks'))!.recordCount).toBe(1);
    expect((await getMeta(farmA, 'tasks'))!.lastSyncedAt).toBe('2026-09-21T07:00:00.000Z');
  });

  it('deletes a collection together with its bookkeeping', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: { title: 'One' } }]);

    await deleteCollection(farmA, 'tasks');

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getMeta(farmA, 'tasks')).toBeNull();
  });

  it('reports only the collections belonging to the asked-for scope', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await replaceCollection(farmA, 'employees', [{ id: 'e-1', data: {} }]);
    await replaceCollection(farmB, 'tasks', [{ id: 't-9', data: {} }]);

    const metas = await getScopeMeta(farmA);

    expect(metas.map((meta) => meta.collection).sort()).toEqual(['employees', 'tasks']);
    expect(metas.every((meta) => meta.farmId === 'farm-a')).toBe(true);
  });

  it('summarises what the device holds, including the freshest sync time', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }], { fetchedAt: '2026-09-19T07:00:00.000Z' });
    await replaceCollection(farmB, 'animals', [
      { id: 'a-1', data: {} },
      { id: 'a-2', data: {} },
    ], { fetchedAt: '2026-09-21T07:00:00.000Z' });

    const stats = await getStats();

    expect(stats.recordCount).toBe(3);
    expect(stats.collectionCount).toBe(2);
    expect(stats.lastSyncedAt).toBe('2026-09-21T07:00:00.000Z');
  });

  it('clears one farm without touching the others', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await replaceCollection(farmB, 'tasks', [{ id: 't-2', data: {} }]);
    await replaceCollection(otherAccount, 'tasks', [{ id: 't-3', data: {} }]);

    await clearFarm(farmA);

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getMeta(farmA, 'tasks')).toBeNull();
    expect(await getCollection(farmB, 'tasks')).toHaveLength(1);
    expect(await getCollection(otherAccount, 'tasks')).toHaveLength(1);
  });

  it('clears one account without touching another', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await replaceCollection(farmB, 'tasks', [{ id: 't-2', data: {} }]);
    await replaceCollection(otherAccount, 'tasks', [{ id: 't-3', data: {} }]);

    await clearAccount('acct-1');

    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await getCollection(farmB, 'tasks')).toHaveLength(0);
    expect(await getCollection(otherAccount, 'tasks')).toHaveLength(1);
  });

  it('empties everything on sign-out', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await replaceCollection(otherAccount, 'animals', [{ id: 'a-1', data: {} }]);

    await clearAll();

    const stats = await getStats();
    expect(stats.recordCount).toBe(0);
    expect(stats.collectionCount).toBe(0);
    expect(stats.lastSyncedAt).toBeNull();
  });

  it('keeps cached rows when the database is reopened', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: { title: 'One' } }]);

    // Simulate a relaunch: drop the cached connection handle and open again, which runs
    // no upgrade because the stored version already matches.
    resetOfflineDbConnection();

    expect((await getRecord(farmA, 'tasks', 't-1'))!.data).toEqual({ title: 'One' });
    expect(await getMeta(farmA, 'tasks')).not.toBeNull();
  });

  /// Old Android WebViews and locked-down browsing modes both reach this code. The app
  /// must behave as "no cache", never as "broken".
  it('degrades to an empty cache when IndexedDB is unavailable', async () => {
    (globalThis as { indexedDB?: unknown }).indexedDB = undefined;
    resetOfflineDbConnection();

    expect(isOfflineStorageAvailable()).toBe(false);
    await expect(putRecords([record(farmA, 'animals', 'a-1', {})])).resolves.toBeUndefined();
    expect(await getCollection(farmA, 'animals')).toEqual([]);
    expect(await getRecord(farmA, 'animals', 'a-1')).toBeNull();
    expect(await getMeta(farmA, 'animals')).toBeNull();
    expect(await getScopeMeta(farmA)).toEqual([]);
    await expect(clearAll()).resolves.toBeUndefined();
    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
  });
});
