import React, { useEffect, useState } from 'react';
import { Alert, Button, Card, Col, Empty, Row, Space, Statistic, Tag, Typography, message } from 'antd';
import { CloudSyncOutlined } from '@ant-design/icons';
import OutboxTable from '../../components/OutboxTable';
import { readCachedCollection } from '../../offline/cachedQuery';
import { kindLabel } from '../../offline/mutationKinds';
import { formatSyncAge } from '../../offline/syncAge';
import { requestFlush } from '../../offline/syncEngine';
import { useSyncStore } from '../../offline/syncStatus';
import {
  queueFullMessage,
  queueWarningMessage,
  sessionBlockedMessage,
  sessionWarningMessage,
} from '../../offline/offlinePolicy';
import { useOfflineStore } from '../../offline/connectivity';
import { useAccountOutboxItems } from '../../offline/useOutbox';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import type { AnimalLookupRow } from '../../api/attendance';
import type { Employee, FarmTask } from '../../types';
import type { OutboxItem } from '../../offline/db';
import { useTranslation } from 'react-i18next';

const { Text, Title } = Typography;

/**
 * What is waiting on this device, and what the server refused.
 *
 * Account-scoped rather than farm-scoped (its route is farm-independent, like the job status
 * view): the queue belongs to the account, and a farm whose access was removed still has items
 * in it that the user needs to see and act on. Items are grouped by farm because that is the
 * unit the sync endpoint works in — and because "three weights for Farm A are stuck" is a
 * different problem from "Farm B has one".
 *
 * Nothing here can lose a record: a refused item is shown with the server's own message, and
 * dismissing one asks for a reason and keeps it on the device.
 */
const SyncStatusPage: React.FC = () => {const { t } = useTranslation('offline'); 
  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farms = useFarmStore((state) => state.farms);
  const isOnline = useOfflineStore((store) => store.isOnline);

  const pendingCount = useSyncStore((state) => state.pendingCount);
  const quarantinedCount = useSyncStore((state) => state.quarantinedCount);
  const oldestQueuedAt = useSyncStore((state) => state.oldestQueuedAt);
  const isFlushing = useSyncStore((state) => state.isFlushing);
  const lastFlushAt = useSyncStore((state) => state.lastFlushAt);
  const lastError = useSyncStore((state) => state.lastError);
  const sessionExpired = useSyncStore((state) => state.sessionExpired);
  const sessionStatus = useSyncStore((state) => state.session.status);
  const daysSinceServerContact = useSyncStore((state) => state.session.daysSinceServerContact);
  const capacity = useSyncStore((state) => state.capacity);
  const refresh = useSyncStore((state) => state.refresh);

  const items = useAccountOutboxItems(accountId);
  const [labels, setLabels] = useState<Record<string, string>>({});

  // The set of farms whose lookups are worth reading, as a string so this effect depends on
  // *which* farms are queued rather than on the array's identity. `items` is a fresh array on
  // every queue event; depending on it re-read the cache per event and cancelled an in-flight
  // read mid-way, which left the labels unresolved.
  const queuedFarmKey = Array.from(new Set(items.map((item) => item.farmId))).sort().join('|');

  // The name behind each queued item, resolved out of its *own* farm's cached collections — the
  // only place the device knows an id and a human label together. Three sources, because a
  // queued weight names an animal, a queued check-in names an employee and a queued completion
  // names a task. An item queued for a farm whose list was never cached shows its id, which is
  // accurate if less friendly.
  useEffect(() => {
    if (!accountId || queuedFarmKey === '') return undefined;

    let active = true;

    void (async () => {
      const resolved: Record<string, string> = {};

      for (const farmId of queuedFarmKey.split('|')) {
        const scope = { accountId, farmId };

        const [animals, employees, tasks] = await Promise.all([
          readCachedCollection<AnimalLookupRow>(scope, 'animals', 'lookup'),
          readCachedCollection<Employee>(scope, 'employees', 'options'),
          readCachedCollection<FarmTask>(scope, 'tasks', 'firstPage'),
        ]);

        for (const row of animals) {
          resolved[row.id] = row.name ? `${row.tagNumber} — ${row.name}` : row.tagNumber;
        }
        for (const row of employees) {
          resolved[row.id] = `${row.firstName} ${row.lastName}`;
        }
        for (const row of tasks) {
          resolved[row.id] = row.title;
        }
      }

      if (active) setLabels(resolved);
    })();

    return () => {
      active = false;
    };
  }, [accountId, queuedFarmKey]);

  const byFarm = new Map<string, OutboxItem[]>();
  for (const item of items) {
    const existing = byFarm.get(item.farmId);
    if (existing) existing.push(item);
    else byFarm.set(item.farmId, [item]);
  }

  const farmName = (farmId: string) =>
    farms.find((farm) => farm.id === farmId)?.name ?? `Farm ${farmId.slice(0, 8)}`;

  /**
   * What is waiting, in the words the pages themselves use ("2 weights, 1 check-in"). The
   * queue now carries workflows with nothing in common but their farm, and "three items" does
   * not tell a user what they are about to lose.
   */
  const kindBreakdown = (farmItems: OutboxItem[]) => {
    const counts = new Map<string, number>();
    for (const item of farmItems) {
      const label = kindLabel(item.kind);
      counts.set(label, (counts.get(label) ?? 0) + 1);
    }

    return Array.from(counts.entries())
      .map(([label, count]) => `${count} ${label.toLowerCase()}${count === 1 ? '' : 's'}`)
      .join(' · ');
  };

  const handleSyncNow = async () => {
    await requestFlush({ force: true });
    await refresh();
    if (useSyncStore.getState().pendingCount === 0) {
      message.success(t('everythingOnThisDeviceHasBeenSent'));
    }
  };

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <div>
        <Title level={4} style={{ marginBottom: 0 }}>Offline &amp; sync</Title>
        <Text type="secondary">
          {t('workRecordedOnThisDeviceIsSentOldest')}
        </Text>
      </div>

      {sessionExpired && (
        <Alert
          type="error"
          showIcon
          message={t('yourSessionExpiredBeforeTheseRecordsCouldBe')}
          description={t('theyAreSafeOnThisDeviceSignIn')}
        />
      )}

      {lastError && (
        <Alert
          type={isOnline ? 'warning' : 'info'}
          showIcon
          message={isOnline ? t('sendingPaused') : t('offline')}
          description={lastError}
        />
      )}

      {/*
        * The two limits, on the screen that is about the queue (5.6). They are shown *before*
        * they bite: a device that only learns its queue is too old when a save is refused has
        * already lost the shift it was recording.
        */}
      {sessionStatus !== 'fresh' && (
        <Alert
          type={sessionStatus === 'blocked' ? 'error' : 'warning'}
          showIcon
          message={sessionStatus === 'blocked'
            ? t('thisDeviceHasGoneTooLongWithoutReaching')
            : t('thisSessionIsGettingOld')}
          description={sessionStatus === 'blocked'
            ? sessionBlockedMessage(daysSinceServerContact ?? 0)
            : sessionWarningMessage(daysSinceServerContact ?? 0)}
        />
      )}

      {capacity.full ? (
        <Alert type="error" showIcon message={t('thisDeviceCannotHoldAnyMoreUnsyncedRecords')} description={queueFullMessage()} />
      ) : capacity.warning ? (
        <Alert type="warning" showIcon message={t('theQueueIsNearlyFull')} description={queueWarningMessage(capacity.remaining)} />
      ) : null}

      <Card>
        <Row gutter={[16, 16]}>
          <Col xs={12} md={6}>
            <Statistic title={t('waitingToSync')} value={pendingCount} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic
              title={t('needsAttention')}
              value={quarantinedCount}
              valueStyle={quarantinedCount > 0 ? { color: '#cf1322' } : undefined}
            />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title={t('oldestWaiting')} value={pendingCount > 0 ? formatSyncAge(oldestQueuedAt) : '—'} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title={t('lastSent')} value={formatSyncAge(lastFlushAt)} />
          </Col>
        </Row>

        <Space style={{ marginTop: 16 }}>
          <Button
            type="primary"
            icon={<CloudSyncOutlined />}
            loading={isFlushing}
            onClick={() => void handleSyncNow()}
          >
            {t('syncNow')}
          </Button>
          <Button onClick={() => void refresh()}>{t('refresh')}</Button>
          {!isOnline && <Tag>{t('offlineSendingResumesWhenTheConnectionIsBack')}</Tag>}
        </Space>
      </Card>

      {items.length === 0 ? (
        <Card>
          <Empty description={t('nothingIsWaitingOnThisDevice')} />
        </Card>
      ) : (
        Array.from(byFarm.entries()).map(([farmId, farmItems]) => (
          <Card
            key={farmId}
            title={farmName(farmId)}
            extra={
              <Text type="secondary">
                {farmItems.filter((item) => item.status === 'pending').length} {t('waiting')}{' '}
                {farmItems.filter((item) => item.status === 'quarantined').length} {t('refused')}
              </Text>
            }
          >
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary">{kindBreakdown(farmItems)}</Text>
            </div>
            <OutboxTable
              items={farmItems}
              targetLabel={(item) => labels[item.targetId] ?? null}
            />
          </Card>
        ))
      )}

      <Text type="secondary">
        {t('recordsStayHereForADayAfterThey')}
      </Text>
    </Space>
  );
};

export default SyncStatusPage;
