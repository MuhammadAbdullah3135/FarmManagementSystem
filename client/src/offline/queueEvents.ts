import { create } from 'zustand';

/**
 * A counter that changes whenever the write queue changed.
 *
 * One leaf module so the store above it (`syncStatus`, for counts) and the hooks beside it
 * (`useOutbox`, for rows) can both react to a write without depending on each other — and so
 * the data layer (`outbox`) can announce its own writes without importing either. A queue UI
 * that had to remember to refresh itself after every enqueue would be one call site away from
 * showing a stale count.
 */
export interface QueueEventState {
  /** Increments on every change to the queue. Never read for its value, only for a change. */
  revision: number;
  noteQueueChanged: () => void;
}

export const useQueueStore = create<QueueEventState>()((set) => ({
  revision: 0,
  noteQueueChanged: () => set((state) => ({ revision: state.revision + 1 })),
}));
