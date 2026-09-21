import React from 'react';
import { Alert, Typography } from 'antd';
import { useOfflineStore } from '../offline/connectivity';

const { Text } = Typography;

export interface OfflineBannerProps {
  /**
   * Injected in tests; defaults to the live connectivity store.
   */
  state?: Pick<ReturnType<typeof useOfflineStore.getState>, 'isOnline' | 'storageAvailable' | 'stats'>;
}

function formatLastSynced(value: string | null): string {
  if (!value) return 'never';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'never';

  const minutes = Math.floor((Date.now() - date.getTime()) / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;

  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? '' : 's'} ago`;
}

/**
 * Tells the truth about what the device can currently do.
 *
 * The wording matters more than the styling: while there is no write queue (that is
 * 4.5.4), an offline banner that implies "your changes will sync later" would be a
 * lie the user only discovers when their morning's work is gone. So this states
 * plainly that saving needs a connection, and separately reports what is actually
 * stored on the device.
 */
const OfflineBanner: React.FC<OfflineBannerProps> = ({ state }) => {
  // Subscribed field by field on purpose: zustand v5 compares selector results by
  // reference, so a selector that builds a fresh object would re-render on every store
  // update and never settle.
  const liveOnline = useOfflineStore((store) => store.isOnline);
  const liveStorageAvailable = useOfflineStore((store) => store.storageAvailable);
  const liveStats = useOfflineStore((store) => store.stats);

  const isOnline = state?.isOnline ?? liveOnline;
  const storageAvailable = state?.storageAvailable ?? liveStorageAvailable;
  const stats = state?.stats ?? liveStats;

  if (isOnline) return null;

  const stored = stats.recordCount > 0
    ? `${stats.recordCount} record${stats.recordCount === 1 ? '' : 's'} stored · last synced ${formatLastSynced(stats.lastSyncedAt)}`
    : storageAvailable
      ? 'Nothing stored on this device yet'
      : 'Offline storage is unavailable on this device';

  return (
    <Alert
      type="warning"
      showIcon
      style={{ marginBottom: 16 }}
      message="Offline"
      description={
        <>
          Saving changes needs a connection — reconnect and try again. <Text type="secondary">{stored}</Text>
        </>
      }
    />
  );
};

export default OfflineBanner;
