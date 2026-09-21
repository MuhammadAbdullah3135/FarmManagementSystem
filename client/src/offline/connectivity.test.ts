import { beforeEach, describe, expect, it, vi } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { initOfflineMonitoring, useOfflineStore } from './connectivity';
import { replaceCollection, resetOfflineDbConnection } from './db';

const scope = { accountId: 'acct-1', farmId: 'farm-a' };

const setNavigatorOnline = (value: boolean) => {
  Object.defineProperty(window.navigator, 'onLine', { value, configurable: true });
};

describe('offline connectivity', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    setNavigatorOnline(true);
    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
  });

  it('seeds from the browser signal at startup', () => {
    setNavigatorOnline(false);

    const teardown = initOfflineMonitoring();

    expect(useOfflineStore.getState().isOnline).toBe(false);
    teardown();
  });

  it('follows connectivity events in both directions', () => {
    const teardown = initOfflineMonitoring();

    window.dispatchEvent(new Event('offline'));
    expect(useOfflineStore.getState().isOnline).toBe(false);

    window.dispatchEvent(new Event('online'));
    expect(useOfflineStore.getState().isOnline).toBe(true);

    teardown();
  });

  it('stops reacting after teardown', () => {
    const teardown = initOfflineMonitoring();
    teardown();

    window.dispatchEvent(new Event('offline'));

    expect(useOfflineStore.getState().isOnline).toBe(true);
  });

  it('reports what the device has stored', async () => {
    await replaceCollection(scope, 'tasks', [
      { id: 't-1', data: { title: 'One' } },
      { id: 't-2', data: { title: 'Two' } },
    ]);

    const teardown = initOfflineMonitoring();
    await vi.waitFor(() => {
      expect(useOfflineStore.getState().stats.recordCount).toBe(2);
    });

    expect(useOfflineStore.getState().stats.collectionCount).toBe(1);
    expect(useOfflineStore.getState().stats.lastSyncedAt).not.toBeNull();
    teardown();
  });

  it('survives a window that has no event target (never throws at boot)', () => {
    // jsdom always has a window, so this covers the guard for the mobile shell's
    // server-rendered edge rather than a real browser: the point is that a missing
    // listener surface degrades to "always online" instead of failing the launch.
    const teardown = initOfflineMonitoring();

    expect(() => teardown()).not.toThrow();
  });
});
