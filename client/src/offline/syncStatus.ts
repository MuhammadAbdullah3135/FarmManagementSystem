import { create } from 'zustand';
import { getCounts } from './outbox';
import { getSessionMarker } from './db';
import {
  queueCapacityFrom,
  sessionStateFrom,
  type OfflineSessionState,
  type QueueCapacityState,
} from './offlinePolicy';
import { useAuthStore } from '../stores/authStore';

/**
 * What the queue is doing right now, for the header badge and the sync screen.
 *
 * Counts are re-read from the store rather than incremented locally: the queue is shared by
 * every tab of the same origin and by a flush that runs outside React, so a derived number
 * that is only adjusted by the code that changed it drifts. `refresh()` is the single writer.
 *
 * No polling: this changes when the queue changes (`initSyncEngine` subscribes the store to
 * the queue's change counter) and when a pass finishes.
 */
export interface SyncStatusState {
  pendingCount: number;
  quarantinedCount: number;
  appliedCount: number;
  /** When the oldest still-pending item was queued — the \"how far behind am I\" number. */
  oldestQueuedAt: string | null;
  isFlushing: boolean;
  /** When a pass last completed without stopping on an error. */
  lastFlushAt: string | null;
  /** Why the last pass stopped early, in the user's words. Cleared on a clean pass. */
  lastError: string | null;
  /** The session died during a flush: items are safe, but sending needs a new sign-in. */
  sessionExpired: boolean;
  /**
   * How close this session is to the point where it can no longer deliver what the device
   * holds (5.6). Refreshed with the counts, so the banner, the record screens and the sync
   * screen all read one value rather than each asking storage on their own render.
   */
  session: OfflineSessionState;
  /** How full the queue is, and how much room is left (5.6). */
  capacity: QueueCapacityState;

  refresh: () => Promise<void>;
  setFlushing: (value: boolean) => void;
  setLastFlushAt: (value: string) => void;
  setError: (message: string | null) => void;
  setSessionExpired: (value: boolean) => void;
}

const empty = {
  pendingCount: 0,
  quarantinedCount: 0,
  appliedCount: 0,
  oldestQueuedAt: null,
};

export const useSyncStore = create<SyncStatusState>()((set) => ({
  ...empty,
  isFlushing: false,
  lastFlushAt: null,
  lastError: null,
  sessionExpired: false,
  session: sessionStateFrom(null),
  capacity: queueCapacityFrom(0),

  refresh: async () => {
    const accountId = useAuthStore.getState().user?.accountId;
    if (!accountId) {
      set({ ...empty, session: sessionStateFrom(null), capacity: queueCapacityFrom(0) });
      return;
    }

    const [counts, marker] = await Promise.all([
      getCounts(accountId),
      getSessionMarker(accountId),
    ]);

    set({
      pendingCount: counts.pending,
      quarantinedCount: counts.quarantined,
      appliedCount: counts.applied,
      oldestQueuedAt: counts.oldestQueuedAt,
      // The same policy the write path applies, computed from the same marker: the screen and
      // the refusal can never disagree about whether the session is too old.
      session: sessionStateFrom(marker?.lastServerContactAt ?? null),
      capacity: queueCapacityFrom(counts.pending),
    });
  },

  setFlushing: (isFlushing) => set({ isFlushing }),
  setLastFlushAt: (lastFlushAt) => set({ lastFlushAt }),
  setError: (lastError) => set({ lastError }),
  setSessionExpired: (sessionExpired) => set({ sessionExpired }),
}));
