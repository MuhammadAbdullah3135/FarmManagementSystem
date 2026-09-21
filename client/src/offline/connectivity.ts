import { create } from 'zustand';
import { getStats, isOfflineStorageAvailable, type OfflineStorageStats } from './db';

/**
 * Whether the device is online, and what is actually sitting in the offline store.
 *
 * `navigator.onLine` is a coarse signal — it reports "there is a network interface",
 * not "the API is reachable" — so it is treated as a hint for *presentation* only
 * (the banner, the offline wording). Anything that must not fail while offline is
 * driven by the request itself failing, never by this flag.
 *
 * No polling: the state changes on the browser's own online/offline events, which is
 * the same reason `AppLayout`'s unread badge avoids an interval.
 */
export interface OfflineState {
  /** Best-effort connectivity hint. Never a precondition for correctness. */
  isOnline: boolean;
  /** False when the WebView has no usable IndexedDB — the app then simply has no cache. */
  storageAvailable: boolean;
  stats: OfflineStorageStats;
  /** Re-read the store's counts and last-synced time (e.g. after a refresh or a clear). */
  refreshStats: () => Promise<void>;
  setOnline: (isOnline: boolean) => void;
}

const emptyStats: OfflineStorageStats = { recordCount: 0, collectionCount: 0, lastSyncedAt: null };

const readInitialOnline = (): boolean => {
  try {
    return typeof navigator === 'undefined' || navigator.onLine !== false;
  } catch {
    return true;
  }
};

export const useOfflineStore = create<OfflineState>()((set) => ({
  isOnline: readInitialOnline(),
  storageAvailable: isOfflineStorageAvailable(),
  stats: emptyStats,

  refreshStats: async () => {
    const stats = await getStats();
    set({ stats, storageAvailable: isOfflineStorageAvailable() });
  },

  setOnline: (isOnline: boolean) => set({ isOnline }),
}));

/**
 * Starts listening for connectivity changes and reads the store's current size.
 *
 * Called once at boot. Returns a teardown so tests (and a future hot-reload) can
 * detach the listeners rather than accumulating them.
 */
export function initOfflineMonitoring(): () => void {
  if (typeof window === 'undefined' || typeof window.addEventListener !== 'function') {
    return () => undefined;
  }

  const goOnline = () => useOfflineStore.getState().setOnline(true);
  const goOffline = () => useOfflineStore.getState().setOnline(false);

  window.addEventListener('online', goOnline);
  window.addEventListener('offline', goOffline);

  // Fired when connectivity is restored after a suspend, which on Android is the
  // difference between "the app noticed" and "the worker waited for a restart".
  useOfflineStore.getState().setOnline(readInitialOnline());
  void useOfflineStore.getState().refreshStats();

  return () => {
    window.removeEventListener('online', goOnline);
    window.removeEventListener('offline', goOffline);
  };
}
