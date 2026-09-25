import React from 'react';
import { Alert, Typography } from 'antd';
import { useOfflineStore } from '../offline/connectivity';
import { useSyncStore } from '../offline/syncStatus';
import { formatSyncAge } from '../offline/syncAge';
import {
  queueCapacityFrom,
  sessionBlockedMessage,
  sessionWarningMessage,
  queueFullMessage,
  queueWarningMessage,
  type OfflineSessionState,
} from '../offline/offlinePolicy';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

export interface OfflineBannerProps {
  /**
   * Injected in tests; defaults to the live connectivity store.
   */
  state?: Pick<ReturnType<typeof useOfflineStore.getState>, 'isOnline' | 'storageAvailable' | 'stats'>;
  /** Injected in tests; defaults to the live queue counts. */
  queue?: { pendingCount: number; quarantinedCount: number };
  /** Injected in tests; defaults to the live session state. */
  session?: OfflineSessionState;
}

/**
 * Tells the truth about what the device can currently do.
 *
 * Since 4.5.4 it can tell the better truth: work is queued rather than refused. The banner says
 * so — with how many records are actually waiting, and how old the cached data is — because the
 * one thing an offline banner must never do is imply a save that will not happen. It also names
 * the records the server has refused, since those need a person, not a connection.
 */
const OfflineBanner: React.FC<OfflineBannerProps> = ({ state, queue, session }) => {const { t } = useTranslation('common'); 
  // Subscribed field by field on purpose: zustand v5 compares selector results by
  // reference, so a selector that builds a fresh object would re-render on every store
  // update and never settle.
  const liveOnline = useOfflineStore((store) => store.isOnline);
  const liveStorageAvailable = useOfflineStore((store) => store.storageAvailable);
  const liveStats = useOfflineStore((store) => store.stats);
  const livePending = useSyncStore((store) => store.pendingCount);
  const liveQuarantined = useSyncStore((store) => store.quarantinedCount);
  const liveSession = useSyncStore((store) => store.session);
  const liveCapacity = useSyncStore((store) => store.capacity);

  const isOnline = state?.isOnline ?? liveOnline;
  const storageAvailable = state?.storageAvailable ?? liveStorageAvailable;
  const stats = state?.stats ?? liveStats;
  const pendingCount = queue?.pendingCount ?? livePending;
  const quarantinedCount = queue?.quarantinedCount ?? liveQuarantined;
  const sessionState = session ?? liveSession;
  // Derived from the count rather than passed in beside it: two props that must agree are two
  // props that can disagree, and the limit is a function of the count by definition.
  const queueCapacity = queue ? queueCapacityFrom(pendingCount) : liveCapacity;

  if (isOnline) return null;

  const stored = stats.recordCount > 0
    ? `${stats.recordCount} record${stats.recordCount === 1 ? '' : 's'} stored · last synced ${formatSyncAge(stats.lastSyncedAt)}`
    : storageAvailable
      ? 'Nothing stored on this device yet'
      : 'Offline storage is unavailable on this device — nothing can be saved until you are back online';

  return (
    <Alert
      type="warning"
      showIcon
      style={{ marginBottom: 16 }}
      message={t('offline')}
      description={
        <>
          {t('changesAreSavedOnThisDeviceAndSent')}
          {pendingCount > 0 && (
            <> <Text strong>{pendingCount} {t('waitingToSync')}</Text></>
          )}
          {quarantinedCount > 0 && (
            <>
              {' '}
              <Text type="danger">
                {quarantinedCount} {t('record')}{quarantinedCount === 1 ? '' : 's'} could not be sent — see
                “Offline &amp; sync”.
              </Text>
            </>
          )}
          {/*
            * The two limits (5.6), said while they are still limits rather than failures. A
            * session that has gone too long without the API is the one case where this banner
            * stops promising that queued work will be delivered — because it may not be.
            */}
          {sessionState.status === 'blocked' && (
            <>
              {' '}
              <Text type="danger">
                {sessionBlockedMessage(sessionState.daysSinceServerContact ?? 0)}
              </Text>
            </>
          )}
          {sessionState.status === 'warning' && (
            <>
              {' '}
              <Text type="warning">
                {sessionWarningMessage(sessionState.daysSinceServerContact ?? 0)}
              </Text>
            </>
          )}
          {queueCapacity.full ? (
            <>
              {' '}
              <Text type="danger">{queueFullMessage()}</Text>
            </>
          ) : queueCapacity.warning ? (
            <>
              {' '}
              <Text type="warning">{queueWarningMessage(queueCapacity.remaining)}</Text>
            </>
          ) : null}
          <br />
          <Text type="secondary">{stored}</Text>
        </>
      }
    />
  );
};

export default OfflineBanner;
