import { beforeEach, describe, expect, it } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import {
  CACHE_STORE,
  META_STORE,
  OFFLINE_DB_NAME,
  clearAccount,
  clearCachedData,
  clearFarm,
  clearQueue,
  deleteCollection,
  deleteOutboxItem,
  getCollection,
  getMeta,
  getOutboxCounts,
  getOutboxItem,
  getRecord,
  getScopeMeta,
  getSessionMarker,
  getStats,
  isOfflineStorageAvailable,
  mergeCollection,
  listOutboxByStatus,
  listOutboxForAccount,
  listOutboxForScope,
  purgeAppliedBefore,
  putOutboxItem,
  putRecords,
  replaceCollection,
  resetOfflineDbConnection,
  touchSessionMarker,
  updateOutboxItem,
  type CachedRecord,
  type OutboxItem,
} from './db';

const twoDaysAgo = '2026-09-21T06:00:00.000Z';

const farmA = { accountId: 'acct-1', farmId: 'farm-a' };
const farmB = { accountId: 'acct-1', farmId: 'farm-b' };
const otherAccount = { accountId: 'acct-2', farmId: 'farm-a' };

const record = (
  scope: { accountId: string; farmId: string },
  collection: string,
  id: string,
  data: unknown,
): CachedRecord => ({ ...scope, collection, id, data, fetchedAt: '2026-09-20T06:00:00.000Z' });

const queued = (
  scope: { accountId: string; farmId: string },
  mutationId: string,
  overrides: Partial<OutboxItem> = {},
): OutboxItem => ({
  ...scope,
  mutationId,
  kind: 'weight.record',
  targetId: 'a-1',
  occurredAt: '2026-09-20T06:00:00.000Z',
  queuedAt: '2026-09-20T06:00:00.000Z',
  payload: { animalId: 'a-1', weightKg: 412, recordedAt: '2026-09-20T06:00:00.000Z' },
  status: 'pending',
  attempts: 0,
  lastError: null,
  serverMessage: null,
  appliedAt: null,
  appliedEntityId: null,
  dismissedReason: null,
  ...overrides,
});

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

  it('empties the read cache on sign-out', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await replaceCollection(otherAccount, 'animals', [{ id: 'a-1', data: {} }]);

    await clearCachedData();

    const stats = await getStats();
    expect(stats.recordCount).toBe(0);
    expect(stats.collectionCount).toBe(0);
    expect(stats.lastSyncedAt).toBeNull();
  });

  it('keeps queued work when the read cache is cleared', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await putOutboxItem(queued(farmA, 'm-1'));

    await clearCachedData();

    // The cache is disposable and the queue is not: signing out must not destroy the only
    // copy of a measurement.
    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
  });

  it('keeps one farm queue when that farm cached data is dropped', async () => {
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);
    await putOutboxItem(queued(farmA, 'm-1'));

    await clearFarm(farmA);

    // Access to the farm is gone, so the rows go — but the queued items stay and are
    // quarantined by the server's own refusal on the next flush.
    expect(await getCollection(farmA, 'tasks')).toHaveLength(0);
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
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
    await expect(clearCachedData()).resolves.toBeUndefined();
    expect(await getStats()).toEqual({ recordCount: 0, collectionCount: 0, lastSyncedAt: null });

    // The queue degrades the same way: an enqueue that cannot be stored reports false rather
    // than pretending the work is safe.
    expect(await putOutboxItem(queued(farmA, 'm-1'))).toBe(false);
    expect(await listOutboxForScope(farmA)).toEqual([]);
    await expect(clearQueue()).resolves.toBeUndefined();
  });
});

describe('outbox store', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
  });

  it('keeps the same mutation id apart across farms and accounts', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));
    await putOutboxItem(queued({ ...farmB }, 'm-1'));
    await putOutboxItem(queued({ ...otherAccount }, 'm-1'));

    expect((await listOutboxForScope(farmA))[0].payload).toMatchObject({ weightKg: 412 });
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
    expect(await listOutboxForAccount('acct-1')).toHaveLength(2);
    expect(await listOutboxForAccount('acct-2')).toHaveLength(1);
  });

  it('orders by queue time and breaks ties on the mutation id', async () => {
    await putOutboxItem(queued(farmA, 'm-b', { queuedAt: '2026-09-20T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'm-a', { queuedAt: '2026-09-20T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'm-c', { queuedAt: '2026-09-19T06:00:00.000Z' }));

    expect((await listOutboxForScope(farmA)).map((item) => item.mutationId)).toEqual([
      'm-c',
      'm-a',
      'm-b',
    ]);
  });

  it('rewrites only the outcome half of an item', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));

    await updateOutboxItem(farmA, 'm-1', (item) => ({
      status: 'quarantined',
      attempts: item.attempts + 1,
      lastError: null,
      serverMessage: 'Weight must be greater than zero',
      appliedAt: null,
      appliedEntityId: null,
      dismissedReason: null,
    }));

    const stored = (await getOutboxItem(farmA, 'm-1'))!;
    expect(stored.status).toBe('quarantined');
    expect(stored.serverMessage).toBe('Weight must be greater than zero');
    // The half the server's idempotency guarantee rests on is untouched by the update.
    expect(stored.payload).toEqual(queued(farmA, 'm-1').payload);
    expect(stored.occurredAt).toBe('2026-09-20T06:00:00.000Z');
    expect(stored.kind).toBe('weight.record');
  });

  it('filters by status without leaking another account items', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));
    await putOutboxItem(queued(farmA, 'm-2', { status: 'quarantined' }));
    await putOutboxItem(queued({ ...otherAccount }, 'm-3'));

    expect((await listOutboxByStatus('acct-1', 'pending')).map((item) => item.mutationId)).toEqual(['m-1']);
    expect((await listOutboxByStatus('acct-1', 'quarantined')).map((item) => item.mutationId)).toEqual(['m-2']);
    expect(await listOutboxByStatus('acct-2', 'quarantined')).toEqual([]);
  });

  it('counts what is waiting and names the oldest queued item', async () => {
    await putOutboxItem(queued(farmA, 'm-1', { queuedAt: '2026-09-19T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'm-2', { queuedAt: '2026-09-20T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'm-3', { status: 'quarantined' }));
    await putOutboxItem(queued(farmA, 'm-4', { status: 'applied', appliedAt: '2026-09-21T06:00:00.000Z' }));

    expect(await getOutboxCounts('acct-1')).toEqual({
      pending: 2,
      quarantined: 1,
      dismissed: 0,
      applied: 1,
      oldestQueuedAt: '2026-09-19T06:00:00.000Z',
    });
  });

  it('reaps applied items only once they are past the cutoff', async () => {
    await putOutboxItem(queued(farmA, 'old', { status: 'applied', appliedAt: '2026-09-20T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'new', { status: 'applied', appliedAt: '2026-09-22T06:00:00.000Z' }));
    await putOutboxItem(queued(farmA, 'pending'));
    await putOutboxItem(queued(farmA, 'refused', { status: 'quarantined' }));

    expect(await purgeAppliedBefore('2026-09-21T06:00:00.000Z')).toBe(1);

    const remaining = (await listOutboxForAccount('acct-1')).map((item) => item.mutationId).sort();
    expect(remaining).toEqual(['new', 'pending', 'refused']);
  });

  it('deletes one item by its full key', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));
    await putOutboxItem(queued(farmB, 'm-1'));

    await deleteOutboxItem(farmA, 'm-1');

    expect(await getOutboxItem(farmA, 'm-1')).toBeNull();
    expect(await getOutboxItem(farmB, 'm-1')).not.toBeNull();
  });
});

describe('offline store upgrades', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
  });

  /**
   * A device that has been using the app since before the queue existed: version 1 created
   * only the cache and syncMeta stores. Opening at the current version must add the outbox and
   * the session marker *and* keep every cached row, which is the whole point of writing the
   * upgrade as per-version steps: a device does not start from nothing because a store was
   * added.
   */
  it('adds the newer stores to an existing version 1 database without disturbing the cache', async () => {
    const previous = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = globalThis.indexedDB.open(OFFLINE_DB_NAME, 1);
      // Built by hand rather than through `upgradeOfflineSchema`, because this is the device
      // state the upgrade must cope with: the version 1 *build* created exactly these two
      // stores, and knew nothing about the outbox.
      request.onupgradeneeded = () => {
        const cached = request.result.createObjectStore(CACHE_STORE, {
          keyPath: ['accountId', 'farmId', 'collection', 'id'],
        });
        cached.createIndex('byScope', ['accountId', 'farmId', 'collection'], { unique: false });
        const meta = request.result.createObjectStore(META_STORE, {
          keyPath: ['accountId', 'farmId', 'collection'],
        });
        meta.createIndex('byAccountFarm', ['accountId', 'farmId'], { unique: false });
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });

    expect(Array.from(previous.objectStoreNames).sort()).toEqual([CACHE_STORE, META_STORE].sort());

    // A row cached by the old build, written through the old store directly.
    await new Promise<void>((resolve, reject) => {
      const tx = previous.transaction([CACHE_STORE], 'readwrite');
      tx.objectStore(CACHE_STORE).put(record(farmA, 'tasks', 't-1', { title: 'From version 1' }));
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
    previous.close();

    // The new build opens the same database at the current version.
    resetOfflineDbConnection();
    expect((await getRecord<{ title: string }>(farmA, 'tasks', 't-1'))!.data.title).toBe('From version 1');

    // And the stores the upgrade added are usable.
    expect(await putOutboxItem(queued(farmA, 'm-1'))).toBe(true);
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
    expect(await touchSessionMarker(farmA.accountId, '2026-09-23T08:00:00.000Z')).not.toBeNull();
  });

  it('creates every store on a fresh device', async () => {
    await putOutboxItem(queued(farmA, 'm-1'));
    await replaceCollection(farmA, 'tasks', [{ id: 't-1', data: {} }]);

    expect(await getStats()).toMatchObject({ recordCount: 1, collectionCount: 1 });
    expect(await listOutboxForScope(farmA)).toHaveLength(1);
    expect(await getSessionMarker(farmA.accountId)).toBeNull();
  });

  // ── deltas (5.6) ─────────────────────────────────────

  describe('mergeCollection', () => {
    const row = (id: string, title: string) => ({ id, data: { id, title } });

    it('keeps rows a delta does not mention, updates the ones it does, and stores the cursor', async () => {
      await replaceCollection(farmA, 'tasks@firstPage', [row('t-1', 'Old'), row('t-2', 'Keep me')], {
        fetchedAt: twoDaysAgo,
        cursor: 'cursor-1',
      });

      await mergeCollection(farmA, 'tasks@firstPage', {
        rows: [row('t-1', 'New')],
        deletedIds: [],
        cursor: 'cursor-2',
      });

      const stored = await getCollection<{ title: string }>(farmA, 'tasks@firstPage');
      expect(stored.map((r) => r.data.title).sort()).toEqual(['Keep me', 'New']);

      const meta = await getMeta(farmA, 'tasks@firstPage');
      expect(meta?.cursor).toBe('cursor-2');
      expect(meta?.recordCount).toBe(2);
      // The row's stored copy moved, not just the collection's sync time.
      expect(stored.find((r) => r.id === 't-1')!.data.title).toBe('New');
    });

    it('removes the ids a delta reports as deleted', async () => {
      await replaceCollection(farmA, 'tasks@firstPage', [row('t-1', 'A'), row('t-2', 'B')], {
        cursor: 'cursor-1',
      });

      await mergeCollection(farmA, 'tasks@firstPage', {
        rows: [],
        deletedIds: ['t-1'],
        cursor: 'cursor-2',
      });

      const stored = await getCollection(farmA, 'tasks@firstPage');
      expect(stored.map((r) => r.id)).toEqual(['t-2']);
      expect((await getMeta(farmA, 'tasks@firstPage'))?.recordCount).toBe(1);
    });

    it('never touches another farm or another account', async () => {
      await replaceCollection(farmA, 'tasks@firstPage', [row('t-1', 'Farm A')], { cursor: 'a-1' });
      await replaceCollection(farmB, 'tasks@firstPage', [row('t-1', 'Farm B')], { cursor: 'b-1' });

      // The same row id in both farms, tombstoned in one of them.
      await mergeCollection(farmA, 'tasks@firstPage', {
        rows: [],
        deletedIds: ['t-1'],
        cursor: 'a-2',
      });

      expect(await getCollection(farmA, 'tasks@firstPage')).toHaveLength(0);
      expect(await getCollection(farmB, 'tasks@firstPage')).toHaveLength(1);
      expect((await getMeta(farmB, 'tasks@firstPage'))?.cursor).toBe('b-1');
    });

    it('records a full read\'s cursor so the next read can be a delta', async () => {
      await replaceCollection(farmA, 'tasks@firstPage', [row('t-1', 'First')], { cursor: 'cursor-1' });

      expect((await getMeta(farmA, 'tasks@firstPage'))?.cursor).toBe('cursor-1');
    });

    it('leaves no cursor when a read does not report one', async () => {
      await replaceCollection(farmA, 'tasks@firstPage', [row('t-1', 'First')]);

      // Null, not a device-generated timestamp: a client-made cursor is the one thing that can
      // skip rows, because it is compared against the server's clock.
      expect((await getMeta(farmA, 'tasks@firstPage'))?.cursor).toBeNull();
    });
  });

  // ── the session marker (5.6) ─────────────────────────

  describe('session marker', () => {
    it('records when the account last reached the API, and never moves backwards', async () => {
      await touchSessionMarker(farmA.accountId, '2026-09-20T08:00:00.000Z');
      expect((await getSessionMarker(farmA.accountId))?.lastServerContactAt)
        .toBe('2026-09-20T08:00:00.000Z');

      await touchSessionMarker(farmA.accountId, '2026-09-23T08:00:00.000Z');
      expect((await getSessionMarker(farmA.accountId))?.lastServerContactAt)
        .toBe('2026-09-23T08:00:00.000Z');

      // Two tabs, or a queued write landing after a fresh one: an older value must not move the
      // offline-write window back and let a stale session keep accepting work.
      await touchSessionMarker(farmA.accountId, '2026-09-21T08:00:00.000Z');
      expect((await getSessionMarker(farmA.accountId))?.lastServerContactAt)
        .toBe('2026-09-23T08:00:00.000Z');
    });

    it('is per account — not per farm — and survives clearing the cache and the queue', async () => {
      await touchSessionMarker(farmA.accountId, '2026-09-23T08:00:00.000Z');
      await touchSessionMarker(otherAccount.accountId, '2026-09-01T08:00:00.000Z');

      // Two farms of one account share one session: reaching the API on either farm is reaching
      // the API, and the account signs in once.
      expect(await getSessionMarker(farmA.accountId)).toEqual(await getSessionMarker(farmB.accountId));

      await clearCachedData();
      await clearQueue();

      // Clearing data must not reset the window that protects the queue: otherwise the way to
      // keep recording past it would be to clear everything first.
      expect((await getSessionMarker(farmA.accountId))?.lastServerContactAt)
        .toBe('2026-09-23T08:00:00.000Z');
      expect((await getSessionMarker(otherAccount.accountId))?.lastServerContactAt)
        .toBe('2026-09-01T08:00:00.000Z');
    });
  });
});
