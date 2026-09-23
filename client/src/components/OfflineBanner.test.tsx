import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import OfflineBanner from './OfflineBanner';
import { MAX_QUEUED_ITEMS, sessionStateFrom } from '../offline/offlinePolicy';

const stats = (overrides: Partial<{ recordCount: number; collectionCount: number; lastSyncedAt: string | null }> = {}) => ({
  recordCount: 0,
  collectionCount: 0,
  lastSyncedAt: null,
  ...overrides,
});

const daysAgo = (days: number) => new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();

/** The offline state with the two 5.6 limits at their defaults unless a test overrides them. */
const offline = (
  overrides: { session?: string | null; pendingCount?: number; quarantinedCount?: number; recordCount?: number } = {},
) => ({
  state: { isOnline: false, storageAvailable: true, stats: stats({ recordCount: overrides.recordCount ?? 0, collectionCount: 1 }) },
  queue: { pendingCount: overrides.pendingCount ?? 0, quarantinedCount: overrides.quarantinedCount ?? 0 },
  session: sessionStateFrom(overrides.session ?? daysAgo(0)),
});

describe('OfflineBanner', () => {
  it('stays out of the way while the device is online', () => {
    render(<OfflineBanner state={{ isOnline: true, storageAvailable: true, stats: stats() }} />);

    expect(screen.queryByText('Offline')).not.toBeInTheDocument();
  });

  /// The honesty rule since 4.5.4: work done offline *is* saved on the device and will be
  /// sent later — and the banner must say how much is actually waiting.
  it('says that changes are kept on the device and how many are waiting', () => {
    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats({ recordCount: 12, collectionCount: 3, lastSyncedAt: new Date(Date.now() - 90 * 60 * 1000).toISOString() }) }}
        queue={{ pendingCount: 3, quarantinedCount: 0 }}
      />,
    );

    expect(screen.getByText('Offline')).toBeInTheDocument();
    expect(screen.getByText(/Changes are saved on this device and sent when the connection is back/)).toBeInTheDocument();
    expect(screen.getByText(/3 waiting to sync/)).toBeInTheDocument();
    expect(screen.getByText(/12 records stored · last synced 1 hour ago/)).toBeInTheDocument();
  });

  it('names the records the server refused, because those need a person', () => {
    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats({ recordCount: 2, collectionCount: 1 }) }}
        queue={{ pendingCount: 0, quarantinedCount: 1 }}
      />,
    );

    expect(screen.getByText(/1 record could not be sent/)).toBeInTheDocument();
    expect(screen.queryByText(/waiting to sync/)).not.toBeInTheDocument();
  });

  it('distinguishes an empty device from an unusable one', () => {
    const { unmount } = render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats() }}
        queue={{ pendingCount: 0, quarantinedCount: 0 }}
      />,
    );
    expect(screen.getByText('Nothing stored on this device yet')).toBeInTheDocument();
    unmount();

    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: false, stats: stats() }}
        queue={{ pendingCount: 0, quarantinedCount: 0 }}
      />,
    );
    expect(screen.getByText(/Offline storage is unavailable on this device/)).toBeInTheDocument();
  });

  it('describes a just-synced cache in the present tense', () => {
    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats({ recordCount: 1, collectionCount: 1, lastSyncedAt: new Date().toISOString() }) }}
        queue={{ pendingCount: 0, quarantinedCount: 0 }}
      />,
    );

    expect(screen.getByText(/1 record stored · last synced just now/)).toBeInTheDocument();
  });

  // ── the two limits (5.6) ───────────────────────────────

  it('says nothing about the session while it is fresh', () => {
    const { state, queue, session } = offline({ session: daysAgo(1) });

    render(<OfflineBanner state={state} queue={queue} session={session} />);

    expect(screen.getByText('Offline')).toBeInTheDocument();
    expect(screen.queryByText(/has not reached the server/)).not.toBeInTheDocument();
  });

  it('warns that the session is close to the point where it cannot deliver, without refusing', () => {
    const { state, queue, session } = offline({ session: daysAgo(5) });

    render(<OfflineBanner state={state} queue={queue} session={session} />);

    expect(screen.getByText(/has not reached the server in 5 days/)).toBeInTheDocument();
    // Still promises the queue will be delivered: it can be, for now.
    expect(screen.getByText(/Changes are saved on this device/)).toBeInTheDocument();
  });

  it('stops promising delivery once new offline writes are refused', () => {
    const { state, queue, session } = offline({ session: daysAgo(8) });

    render(<OfflineBanner state={state} queue={queue} session={session} />);

    const blocked = screen.getByText(/has not reached the server in 8 days/);
    expect(blocked).toBeInTheDocument();
    // And it says what to do about it, because a limit with no way out is just a failure.
    expect(blocked.textContent).toMatch(/sync/i);
  });

  it('warns before the queue is full, and says so when it is', () => {
    const nearlyFull = offline({ pendingCount: MAX_QUEUED_ITEMS - 100 });
    const { unmount } = render(
      <OfflineBanner state={nearlyFull.state} queue={nearlyFull.queue} session={nearlyFull.session} />,
    );
    expect(screen.getByText(/almost as many unsynced records as it can \(100 left\)/)).toBeInTheDocument();
    unmount();

    const full = offline({ pendingCount: MAX_QUEUED_ITEMS });
    render(<OfflineBanner state={full.state} queue={full.queue} session={full.session} />);
    expect(screen.getByText(/5,000 unsynced records, its limit/)).toBeInTheDocument();
  });
});
