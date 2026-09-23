import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { CACHED_COLLECTIONS, useCachedQuery } from './cachedQuery';
import { getCollection, getMeta, getStats, replaceCollection, resetOfflineDbConnection } from './db';
import { useOfflineStore } from './connectivity';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

interface TaskRow {
  id: string;
  title: string;
}

const account = 'acct-1';
const farmA: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};
const farmB: Farm = { ...farmA, id: 'farm-b', name: 'Farm B' };
const scopeA = { accountId: account, farmId: farmA.id };
const scopeB = { accountId: account, farmId: farmB.id };

const user: User = {
  userId: 'user-1',
  accountId: account,
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['FarmManager'],
};

const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString();

const setSession = (farm: Farm) => {
  useAuthStore.setState({ user, isAuthenticated: true });
  useFarmStore.setState({ farms: [farmA, farmB], activeFarm: farm, isLoading: false, error: null });
};

/** The storage entry shape `replaceCollection` takes — not the API's row shape. */
const storedTask = (id: string, title: string) => ({ id, data: { id, title } });

/** The API's row shape, as a fetcher returns it. */
const task = (id: string, title: string): TaskRow => ({ id, title });

describe('useCachedQuery', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    setSession(farmA);
  });

  it('serves the last-known rows offline without touching the network', async () => {
    useOfflineStore.setState({ isOnline: false });
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('t-1', 'Repair the fence')], {
      fetchedAt: twoDaysAgo,
    });

    const fetcher = vi.fn();
    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    expect(result.current.rows[0].title).toBe('Repair the fence');
    expect(result.current.lastSyncedAt).toBe(twoDaysAgo);
    expect(result.current.fromCache).toBe(true);
    expect(fetcher).not.toHaveBeenCalled();
  });

  it('paints the cache first, then revalidates and re-stamps it', async () => {
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('t-old', 'Cached title')], {
      fetchedAt: twoDaysAgo,
    });

    let resolveFetch!: (value: { rows: TaskRow[]; total: number }) => void;
    const fetcher = vi.fn(
      () =>
        new Promise<{ rows: TaskRow[]; total: number }>((resolve) => {
          resolveFetch = resolve;
        }),
    );

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );

    // The stale row is on screen while the request is still in flight.
    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Cached title'));
    expect(result.current.fromCache).toBe(true);

    await act(async () => {
      resolveFetch({ rows: [{ id: 't-new', title: 'Fresh title' }], total: 42 });
    });

    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Fresh title'));
    expect(result.current.total).toBe(42);
    expect(result.current.fromCache).toBe(false);

    // The store now holds the server's rows, and the collection's sync time moved on.
    const stored = await getCollection<TaskRow>(scopeA, 'tasks@firstPage');
    expect(stored.map((record) => record.data.title)).toEqual(['Fresh title']);
    const meta = await getMeta(scopeA, 'tasks@firstPage');
    expect(meta?.recordCount).toBe(1);
    expect(meta?.lastSyncedAt).not.toBe(twoDaysAgo);
    expect(meta?.lastSyncedAt).toBe(result.current.lastSyncedAt);
  });

  it('degrades to an empty view offline when nothing is cached, without throwing', async () => {
    useOfflineStore.setState({ isOnline: false });

    const fetcher = vi.fn();
    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.rows).toEqual([]);
    expect(result.current.lastSyncedAt).toBeNull();
    expect(fetcher).not.toHaveBeenCalled();
  });

  it('fetches and stores the default view the first time it loads online', async () => {
    const fetcher = vi.fn().mockResolvedValue({ rows: [task('t-1', 'First load')], total: 5 });

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    expect(result.current.total).toBe(5);
    expect(await getCollection(scopeA, 'tasks@firstPage')).toHaveLength(1);
    // The banner's counts follow what was stored.
    expect((await getStats()).collectionCount).toBe(1);
  });

  it('never shows one farm rows under another when the active farm changes', async () => {
    useOfflineStore.setState({ isOnline: false });
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('a-1', 'Farm A task')], {
      fetchedAt: twoDaysAgo,
    });
    await replaceCollection(scopeB, 'tasks@firstPage', [storedTask('b-1', 'Farm B task')], {
      fetchedAt: twoDaysAgo,
    });

    const fetcher = vi.fn();
    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );
    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Farm A task'));

    act(() => {
      useFarmStore.getState().setActiveFarm(farmB);
    });

    // The first render under farm B must not carry farm A's row: the scope guard is
    // applied during render, before farm B's cache read has resolved.
    expect(result.current.rows.map((row) => row.title)).not.toContain('Farm A task');

    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Farm B task'));
    expect(result.current.rows.map((row) => row.title)).not.toContain('Farm A task');
  });

  it('does not read or write the store for a view that is not cacheable', async () => {
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('t-1', 'Cached default page')], {
      fetchedAt: twoDaysAgo,
    });

    const fetcher = vi.fn().mockResolvedValue({ rows: [task('t-9', 'Filtered result')], total: 1 });
    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({
        collection: 'tasks',
        variant: 'firstPage',
        cacheable: false,
        paramsKey: 'filtered',
        fetcher,
      }),
    );

    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Filtered result'));
    // Not the cached default page (no store read), and no sync time to advertise.
    expect(result.current.fromCache).toBe(false);
    expect(result.current.lastSyncedAt).toBeNull();

    // Only the seeded default page is in the store: the filtered result was never saved.
    expect((await getStats()).recordCount).toBe(1);
  });

  it('re-runs the query when its parameters change', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce({ rows: [task('p1', 'Page one')], total: 30 })
      .mockResolvedValueOnce({ rows: [task('p2', 'Page two')], total: 30 });

    const { result, rerender } = renderHook(
      ({ page }: { page: number }) =>
        useCachedQuery<TaskRow>({
          collection: 'tasks',
          variant: 'firstPage',
          cacheable: page === 1,
          paramsKey: `page:${page}`,
          fetcher,
        }),
      { initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Page one'));

    rerender({ page: 2 });
    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Page two'));
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it('re-runs the query on refresh and re-stamps the cache', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce({ rows: [task('t-1', 'Before mutation')], total: 1 })
      .mockResolvedValueOnce({ rows: [task('t-1', 'After mutation')], total: 1 });

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher }),
    );
    await waitFor(() => expect(result.current.rows[0]?.title).toBe('Before mutation'));

    act(() => {
      result.current.refresh();
    });

    await waitFor(() => expect(result.current.rows[0]?.title).toBe('After mutation'));
    expect((await getCollection<TaskRow>(scopeA, 'tasks@firstPage'))[0].data.title).toBe('After mutation');
  });

  it('reports a failed request instead of throwing', async () => {
    const onError = vi.fn();
    const failure = new Error('network down');
    const fetcher = vi.fn().mockRejectedValue(failure);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', fetcher, onError }),
    );

    await waitFor(() => expect(onError).toHaveBeenCalledWith(failure));
    expect(result.current.rows).toEqual([]);
  });

  it('stores rows under the id the caller chooses', async () => {
    // Weight-check status rows carry no id of their own; their animal is the identity.
    const fetcher = vi.fn().mockResolvedValue({
      rows: [{ animalId: 'animal-1', status: 'Due' }],
      total: 1,
    });

    const { result } = renderHook(() =>
      useCachedQuery<{ animalId: string; status: string }>({
        collection: 'weightCheckStatus',
        variant: 'all',
        getRowId: (row) => row.animalId,
        fetcher,
      }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    const stored = await getCollection(scopeA, 'weightCheckStatus@all');
    expect(stored.map((record) => record.id)).toEqual(['animal-1']);
  });

  it('refuses a collection that is not registered as cacheable', () => {
    // The cached surface is an approved list; anything else must be a deliberate decision
    // rather than an accidental device-wide mirror of the API. `animals@lookup` is registered
    // (5.4's cache-only animal lookup) — `animals@all` deliberately is not.
    expect(() =>
      renderHook(() =>
        useCachedQuery({ collection: 'animals' as never, variant: 'all', fetcher: vi.fn() }),
      ),
    ).toThrow(/not a registered cached variant/);
  });

  it('declares exactly the approved cached surface', () => {
    // Pinned on purpose: every collection a device mirrors of the API is a decision about what
    // may live on a stolen handset and what a delta must keep in step. A sixth entry is
    // therefore a diff somebody has to approve, not an accident review has to notice.
    expect(CACHED_COLLECTIONS).toEqual({
      weightCheckStatus: ['all'],
      tasks: ['firstPage'],
      employees: ['listPage1', 'options'],
      animals: ['lookup'],
      attendance: ['firstPage'],
    });
  });
});

/**
 * The delta path (5.6): a cached collection whose endpoint can answer "what changed since you
 * last asked" reads the cursor out of the store, merges what comes back, and removes what the
 * server says is gone — instead of replacing the collection with whatever the delta mentioned.
 */
describe('useCachedQuery delta reads', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    setSession(farmA);
  });

  /** A fetcher that answers as the API's `DeltaResult` does, one response per call. */
  const deltaFetcher = (responses: {
    rows: TaskRow[];
    total?: number;
    cursor: string;
    delta?: { deletedIds: string[]; requiresFullSync: boolean };
  }[]) => vi.fn(async () => {
    const next = responses.shift();
    if (!next) throw new Error('no response left');
    return {
      rows: next.rows,
      total: next.total ?? next.rows.length,
      cursor: next.cursor,
      delta: next.delta,
    };
  });

  it('sends the stored cursor, merges the answer, and keeps the rows it already had', async () => {
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('t-1', 'Repair the fence')], {
      fetchedAt: twoDaysAgo,
      cursor: 'cursor-1',
    });

    const fetcher = deltaFetcher([
      {
        rows: [task('t-2', 'Order feed')],
        cursor: 'cursor-2',
        delta: { deletedIds: [], requiresFullSync: false },
      },
    ]);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', supportsDelta: true, fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(2));

    // The request carried the server's cursor, not the device's clock.
    expect(fetcher).toHaveBeenCalledWith('cursor-1');
    // The unchanged row survived: a delta is not a page of rows, it is a change set.
    expect(result.current.rows.map((r) => r.id).sort()).toEqual(['t-1', 't-2']);

    const meta = await getMeta(scopeA, 'tasks@firstPage');
    expect(meta?.cursor).toBe('cursor-2');
    expect(meta?.recordCount).toBe(2);
  });

  it('removes the rows the server reports as deleted', async () => {
    await replaceCollection(
      scopeA,
      'tasks@firstPage',
      [storedTask('t-1', 'Repair the fence'), storedTask('t-2', 'Order feed')],
      { fetchedAt: twoDaysAgo, cursor: 'cursor-1' },
    );

    const fetcher = deltaFetcher([
      {
        rows: [],
        cursor: 'cursor-2',
        delta: { deletedIds: ['t-1'], requiresFullSync: false },
      },
    ]);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', supportsDelta: true, fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    expect(result.current.rows[0].id).toBe('t-2');
    // Gone from the store too, not just from the render: a reload must not resurrect it.
    expect((await getCollection<TaskRow>(scopeA, 'tasks@firstPage')).map((r) => r.id)).toEqual(['t-2']);
  });

  it('reads the whole collection when the server cannot answer the cursor', async () => {
    await replaceCollection(
      scopeA,
      'tasks@firstPage',
      [storedTask('t-1', 'Stale')],
      { fetchedAt: twoDaysAgo, cursor: 'ancient' },
    );

    const fetcher = deltaFetcher([
      // The server's answer to a cursor it cannot use.
      { rows: [], cursor: 'cursor-2', delta: { deletedIds: [], requiresFullSync: true } },
      // Which the hook follows with exactly one full read.
      {
        rows: [task('t-9', 'Everything'), task('t-8', 'Also everything')],
        cursor: 'cursor-3',
        delta: { deletedIds: [], requiresFullSync: false },
      },
    ]);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', supportsDelta: true, fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(2));

    expect(fetcher).toHaveBeenNthCalledWith(1, 'ancient');
    // The re-read is asked in full — no cursor — and it *replaces* what was stored.
    expect(fetcher).toHaveBeenNthCalledWith(2, null);
    expect(fetcher).toHaveBeenCalledTimes(2);
    expect(result.current.rows.map((r) => r.id).sort()).toEqual(['t-8', 't-9']);
    expect((await getCollection<TaskRow>(scopeA, 'tasks@firstPage')).map((r) => r.id).sort())
      .toEqual(['t-8', 't-9']);
    expect((await getMeta(scopeA, 'tasks@firstPage'))?.cursor).toBe('cursor-3');
  });

  it('asks in full and stores the cursor the first time a collection is read', async () => {
    const fetcher = deltaFetcher([
      {
        rows: [task('t-1', 'First')],
        cursor: 'cursor-1',
        delta: { deletedIds: [], requiresFullSync: false },
      },
    ]);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', supportsDelta: true, fetcher }),
    );

    await waitFor(() => expect(result.current.rows).toHaveLength(1));

    // Nothing stored means no cursor to send, so the first read is the whole collection — and
    // it leaves the cursor behind, which is what lets the second read be a delta.
    expect(fetcher).toHaveBeenCalledWith(null);
    expect((await getMeta(scopeA, 'tasks@firstPage'))?.cursor).toBe('cursor-1');
  });

  it('never sends another farm’s cursor', async () => {
    await replaceCollection(scopeA, 'tasks@firstPage', [storedTask('a-1', 'Farm A task')], {
      fetchedAt: twoDaysAgo,
      cursor: 'cursor-a',
    });
    await replaceCollection(scopeB, 'tasks@firstPage', [storedTask('b-1', 'Farm B task')], {
      fetchedAt: twoDaysAgo,
      cursor: 'cursor-b',
    });

    const fetcher = deltaFetcher([
      { rows: [task('a-2', 'Farm A newer')], cursor: 'cursor-a2', delta: { deletedIds: [], requiresFullSync: false } },
      { rows: [task('b-2', 'Farm B newer')], cursor: 'cursor-b2', delta: { deletedIds: [], requiresFullSync: false } },
    ]);

    const { result } = renderHook(() =>
      useCachedQuery<TaskRow>({ collection: 'tasks', variant: 'firstPage', supportsDelta: true, fetcher }),
    );
    await waitFor(() => expect(result.current.rows.map((r) => r.id).sort()).toEqual(['a-1', 'a-2']));

    act(() => {
      useFarmStore.getState().setActiveFarm(farmB);
    });

    await waitFor(() => expect(result.current.rows.map((r) => r.id).sort()).toEqual(['b-1', 'b-2']));

    // Each farm is asked with its own cursor — the store's scope is the collection key, so the
    // cursor a farm sends back can only ever be the one the server gave for that farm.
    expect(fetcher).toHaveBeenNthCalledWith(1, 'cursor-a');
    expect(fetcher).toHaveBeenNthCalledWith(2, 'cursor-b');
    expect((await getMeta(scopeA, 'tasks@firstPage'))?.cursor).toBe('cursor-a2');
    expect((await getMeta(scopeB, 'tasks@firstPage'))?.cursor).toBe('cursor-b2');
  });
});
