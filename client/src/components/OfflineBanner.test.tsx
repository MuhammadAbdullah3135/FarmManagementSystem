import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import OfflineBanner from './OfflineBanner';

const stats = (overrides: Partial<{ recordCount: number; collectionCount: number; lastSyncedAt: string | null }> = {}) => ({
  recordCount: 0,
  collectionCount: 0,
  lastSyncedAt: null,
  ...overrides,
});

describe('OfflineBanner', () => {
  it('stays out of the way while the device is online', () => {
    render(<OfflineBanner state={{ isOnline: true, storageAvailable: true, stats: stats() }} />);

    expect(screen.queryByText('Offline')).not.toBeInTheDocument();
  });

  /// The honesty rule: until the write queue exists, the banner must not imply that
  /// work done offline will be saved later.
  it('says that saving needs a connection', () => {
    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats({ recordCount: 12, collectionCount: 3, lastSyncedAt: new Date(Date.now() - 90 * 60 * 1000).toISOString() }) }}
      />,
    );

    expect(screen.getByText('Offline')).toBeInTheDocument();
    expect(screen.getByText(/Saving changes needs a connection/)).toBeInTheDocument();
    expect(screen.getByText(/12 records stored · last synced 1 hour ago/)).toBeInTheDocument();
  });

  it('distinguishes an empty device from an unusable one', () => {
    const { unmount } = render(
      <OfflineBanner state={{ isOnline: false, storageAvailable: true, stats: stats() }} />,
    );
    expect(screen.getByText('Nothing stored on this device yet')).toBeInTheDocument();
    unmount();

    render(<OfflineBanner state={{ isOnline: false, storageAvailable: false, stats: stats() }} />);
    expect(screen.getByText('Offline storage is unavailable on this device')).toBeInTheDocument();
  });

  it('describes a just-synced cache in the present tense', () => {
    render(
      <OfflineBanner
        state={{ isOnline: false, storageAvailable: true, stats: stats({ recordCount: 1, collectionCount: 1, lastSyncedAt: new Date().toISOString() }) }}
      />,
    );

    expect(screen.getByText(/1 record stored · last synced just now/)).toBeInTheDocument();
  });
});
