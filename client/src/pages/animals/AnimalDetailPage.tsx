import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Card, Descriptions, Tag, Tabs, Table, Button, Space, Spin, Typography, message, Breadcrumb, Empty } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { animalsApi, type WeightRecord } from '../../api/animals';
import { weightCheckStatusApi } from '../../api/health';
import { breedingRecordsApi } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import { useCachedQuery } from '../../offline/cachedQuery';
import { useOutboxItems } from '../../offline/useOutbox';
import { WEIGHT_RECORD } from '../../offline/mutationKinds';
import { useQueueStore } from '../../offline/queueEvents';
import SyncAgeLabel from '../../components/SyncAgeLabel';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import { formatDate } from '../../i18n/format';
import dayjs from 'dayjs';
import type { AnimalDetail, BreedingRecord, WeightCheckStatus } from '../../types';
import { useTranslation } from 'react-i18next';

/** One row of the merged weights view: the server's copy, or this device's queued one. */
interface WeightRow {
  key: string;
  takenAt: string;
  weightKg: number;
  notes?: string | null;
  status: 'server' | 'pending' | 'quarantined' | 'synced';
  message?: string | null;
}

const { Text } = Typography;

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

/**
 * The weights tab: weight-check status, and the animal's weights.
 *
 * Three sources, merged deliberately rather than shown as three lists:
 *
 * - the **server's** records, fetched when the device can reach the API;
 * - this device's **queued** records for this animal, which appear the instant they are
 *   recorded and stay until the server has them (4.5.4's optimistic row);
 * - records **applied in the last day**, kept from the queue so a row does not vanish between
 *   "sent" and "the server list says so". A row the server list already shows is not repeated.
 *
 * Reading the status list still goes through the cached query (Phase 5.2), so offline the
 * statuses stay on screen with a label saying how old they are.
 */
const AnimalWeightsTab: React.FC<{ animalId: string; weightRecordsCount: number }> = ({
  animalId,
  weightRecordsCount,
}) => {const { t } = useTranslation('animals'); 
  const navigate = useNavigate();
  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farmId = useFarmStore((state) => state.activeFarm?.id ?? null);
  const scope = accountId && farmId ? { accountId, farmId } : null;
  const revision = useQueueStore((state) => state.revision);

  const [serverRows, setServerRows] = useState<WeightRecord[]>([]);

  const loadWeights = useCallback(async () => {
    try {
      const response = await animalsApi.getWeights(animalId, 1, 20);
      setServerRows(response.data.items ?? []);
    } catch {
      // Offline, or the request failed: the queued rows below are still what this device
      // knows for certain, and an empty server list is honest.
      setServerRows([]);
    }
  }, [animalId]);

  // Re-reads after any queue change: a record that just synced has to turn into the server's
  // row without a manual refresh. Deferred like the animal load below, so the effect body does
  // not start a render of its own.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadWeights();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [loadWeights, revision]);

  // This animal's queued weights, and nothing else: the queue now carries other workflows, so
  // the filter is the hook's rather than a condition at this call site.
  const queuedRows = useOutboxItems(scope, { kinds: [WEIGHT_RECORD] }).filter(
    (item) => item.targetId === animalId,
  );

  const serverIds = new Set(serverRows.map((row) => row.id));

  const rows: WeightRow[] = [
    ...serverRows.map((row) => ({
      key: row.id,
      takenAt: row.recordedAt,
      weightKg: row.weightKg,
      notes: row.notes,
      status: 'server' as const,
    })),
    ...queuedRows
      // An applied row the server list already carries would be a duplicate of itself.
      .filter((item) => !(item.status === 'applied' && item.appliedEntityId && serverIds.has(item.appliedEntityId)))
      .map((item) => ({
        key: item.mutationId,
        takenAt: item.occurredAt,
        weightKg: (item.payload as { weightKg: number }).weightKg,
        notes: (item.payload as { notes?: string | null }).notes ?? null,
        status: item.status === 'applied'
          ? ('synced' as const)
          : item.status === 'quarantined'
            ? ('quarantined' as const)
            : ('pending' as const),
        message: item.serverMessage ?? item.lastError,
      })),
  ].sort((a, b) => b.takenAt.localeCompare(a.takenAt));

  const weightQuery = useCachedQuery<WeightCheckStatus>({
    collection: 'weightCheckStatus',
    variant: 'all',
    // Delta-capable: the projection is large and mostly unchanged between visits.
    supportsDelta: true,
    // These rows carry no id of their own; the animal is their identity.
    getRowId: (row) => row.animalId,
    fetcher: async (cursor) => {
      const res = await weightCheckStatusApi.all(cursor);
      const data = res.data;
      return {
        rows: data.items,
        total: data.totalCount,
        // Stored on a full read too — it is what makes the *next* read a delta.
        cursor: data.cursor,
        // Present whenever the request carried a cursor: the hook then merges rather than
        // replaces, so an animal whose status did not change keeps the row it already has.
        delta: cursor ? {
          deletedIds: data.deletedIds ?? [],
          requiresFullSync: data.requiresFullSync,
        } : undefined,
      };
    },
    // Supplementary tab data: a failure must not interrupt the page (as before).
    onError: () => undefined,
  });

  const statuses = weightQuery.rows.filter((status) => status.animalId === animalId);

  return (
    <div>
      {weightQuery.lastSyncedAt && (
        <div style={{ marginBottom: 8 }}>
          <SyncAgeLabel lastSyncedAt={weightQuery.lastSyncedAt} />
        </div>
      )}
      {statuses.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <strong>{t('weightCheckStatus')}</strong>
          <div style={{ marginTop: 8 }}>
            {statuses.map((ws, i) => (
              <Tag
                key={i}
                color={ws.status === 'Overdue' ? 'red' : ws.status === 'Due' ? 'orange' : 'blue'}
                style={{ marginBottom: 4 }}
              >
                {ws.status} {t('nextDue')} {formatDate(ws.nextDueDate)} ({ws.daysUntilDue} {t('days')}
              </Tag>
            ))}
          </div>
        </div>
      )}
      <div style={{ marginBottom: 12 }}>
        <Space>
          <Button
            type="primary"
            onClick={() => navigate(`/dashboard/records/weight?animalId=${animalId}`)}
          >
            {t('recordWeight')}
          </Button>
          <Text type="secondary">
            {t('worksOfflineTheWeightIsStoredOnThis')}
          </Text>
        </Space>
      </div>

      {statuses.length === 0 && rows.length === 0 ? (
        <Empty description={`Weight records: ${weightRecordsCount}`} />
      ) : (
        <>
          <Table
            rowKey="key"
            size="small"
            pagination={false}
            dataSource={rows}
            locale={{ emptyText: `No weights loaded (server count: ${weightRecordsCount})` }}
            columns={[
              {
                title: 'Taken',
                key: 'takenAt',
                render: (_: unknown, row: WeightRow) => dayjs(row.takenAt).format('YYYY-MM-DD HH:mm'),
              },
              {
                title: 'Weight',
                key: 'weightKg',
                render: (_: unknown, row: WeightRow) => `${row.weightKg} kg`,
              },
              {
                title: 'Notes',
                dataIndex: 'notes',
                key: 'notes',
                render: (notes?: string | null) => notes || '-',
              },
              {
                title: 'Status',
                key: 'status',
                render: (_: unknown, row: WeightRow) => {
                  if (row.status === 'server') return <Tag>{t('saved')}</Tag>;
                  if (row.status === 'synced') return <Tag color="green">{t('synced')}</Tag>;
                  if (row.status === 'pending') return <Tag color="blue">{t('waitingToSync')}</Tag>;
                  return (
                    <Space direction="vertical" size={0}>
                      <Tag color="red">{t('notSaved')}</Tag>
                      {row.message && <Text type="danger">{row.message}</Text>}
                    </Space>
                  );
                },
              },
            ]}
          />
          <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            {rows.filter((row) => row.status === 'pending' || row.status === 'quarantined').length > 0
              ? t('recordsMarkedWaitingToSyncExistOnlyOn')
              : `Weight records: ${weightRecordsCount}`}
          </Text>
        </>
      )}
    </div>
  );
};

export default function AnimalDetailPage() {const { t } = useTranslation('animals'); 
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [animal, setAnimal] = useState<AnimalDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [breedingRecords, setBreedingRecords] = useState<BreedingRecord[]>([]);
  const [breedingLoading, setBreedingLoading] = useState(false);

  const loadAnimal = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const res = await animalsApi.get(id);
      setAnimal(res.data as unknown as AnimalDetail);
    } catch (err) {
      message.error(getApiError(err));
      navigate('/dashboard/animals');
    } finally {
      setLoading(false);
    }
  }, [id, navigate]);

  const loadBreeding = useCallback(async () => {
    if (!id) return;
    setBreedingLoading(true);
    try {
      const res = await breedingRecordsApi.list({ damId: id, pageSize: 50 });
      const sireRes = await breedingRecordsApi.list({ sireId: id, pageSize: 50 });
      const all = [...(res.data.items || []), ...(sireRes.data.items || [])];
      const unique = all.filter((r, i, arr) => arr.findIndex(x => x.id === r.id) === i);
      setBreedingRecords(unique.sort((a, b) => new Date(b.breedingDate).getTime() - new Date(a.breedingDate).getTime()));
    } catch {
      // Silently fail - breeding tab is optional
    } finally {
      setBreedingLoading(false);
    }
  }, [id]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadAnimal(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadAnimal]);

  const handleTabChange = (key: string) => {
    if (key === 'breeding') loadBreeding();
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (!animal) return null;

  const methodLabels: Record<number, string> = { 0: 'Natural', 1: 'AI' };
  const resultLabels: Record<number, string> = { 0: 'Pending', 1: 'Confirmed', 2: 'Failed' };
  const resultColors: Record<number, string> = { 0: 'orange', 1: 'green', 2: 'red' };

  const breedingColumns = [
    {
      title: t('date'),
      dataIndex: 'breedingDate',
      key: 'breedingDate',
      render: (text: string) => formatDate(text),
    },
    {
      title: t('sire'),
      key: 'sire',
      render: (_: unknown, record: BreedingRecord) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.sireId}`)}>
          {record.sireName ? `${record.sireTagNumber} - ${record.sireName}` : record.sireTagNumber}
        </a>
      ),
    },
    {
      title: t('dam'),
      key: 'dam',
      render: (_: unknown, record: BreedingRecord) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.damId}`)}>
          {record.damName ? `${record.damTagNumber} - ${record.damName}` : record.damTagNumber}
        </a>
      ),
    },
    {
      title: t('method'),
      dataIndex: 'method',
      key: 'method',
      render: (val: number) => methodLabels[val] || 'Unknown',
    },
    {
      title: t('result'),
      dataIndex: 'result',
      key: 'result',
      render: (val: number) => <Tag color={resultColors[val]}>{resultLabels[val]}</Tag>,
    },
    {
      title: t('vet'),
      dataIndex: 'vetName',
      key: 'vetName',
      render: (text: string) => text || '-',
    },
  ];

  const tabItems = [
    {
      key: 'overview',
      label: t('overview'),
      children: (
        <Descriptions bordered column={2} size="small">
          <Descriptions.Item label={t('tagNumber')}>{animal.tagNumber}</Descriptions.Item>
          <Descriptions.Item label={t('name')}>{animal.name || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('type')}>{animal.animalTypeName}</Descriptions.Item>
          <Descriptions.Item label={t('breed')}>{animal.breedName || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('sex')}>{animal.sexValue}</Descriptions.Item>
          <Descriptions.Item label={t('status')}>
            <Tag color={STATUS_COLORS[animal.statusCategory] || 'default'}>{animal.statusName}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('location')}>{animal.locationName || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('ageCategory')}>{animal.ageCategoryName || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('dateOfBirth')}>{animal.dateOfBirth ? formatDate(animal.dateOfBirth) : '-'}</Descriptions.Item>
          <Descriptions.Item label={t('acquisitionDate')}>{animal.acquisitionDate ? formatDate(animal.acquisitionDate) : '-'}</Descriptions.Item>
          <Descriptions.Item label={t('sire')}>{animal.sireTagNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('dam')}>{animal.damTagNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('notes')} span={2}>{animal.notes || '-'}</Descriptions.Item>
          <Descriptions.Item label={t('weightRecords')}>{animal.weightRecordsCount}</Descriptions.Item>
          <Descriptions.Item label={t('images')}>{animal.imagesCount}</Descriptions.Item>
          <Descriptions.Item label={t('created')}>{dayjs(animal.createdAt).format('YYYY-MM-DD HH:mm')}</Descriptions.Item>
        </Descriptions>
      ),
    },
    {
      key: 'weights',
      label: t('weights'),
      children: (
        <AnimalWeightsTab
          animalId={animal.id}
          weightRecordsCount={animal.weightRecordsCount}
        />
      ),
    },
    {
      key: 'timeline',
      label: t('timeline'),
      children: (
        <Empty description={t('timelineEventsWillAppearHere')} />
      ),
    },
    {
      key: 'breeding',
      label: t('breeding'),
      children: (
        <div>
          <div style={{ marginBottom: 16 }}>
            <Button type="primary" onClick={() => navigate('/dashboard/breeding/records')}>
              {t('addBreedingRecord')}
            </Button>
          </div>
          <Table
            rowKey="id"
            columns={breedingColumns}
            dataSource={breedingRecords}
            loading={breedingLoading}
            pagination={false}
            size="small"
          />
        </div>
      ),
    },
  ];

  return (
    <div>
      <Breadcrumb
        style={{ marginBottom: 16 }}
        items={[
          { title: <a onClick={() => navigate('/dashboard/animals')}>{t('animals')}</a> },
          { title: `${animal.tagNumber}${animal.name ? ` - ${animal.name}` : ''}` },
        ]}
      />
      <Card
        title={`${animal.tagNumber}${animal.name ? ` - ${animal.name}` : ''}`}
        extra={
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/dashboard/animals')}>
            {t('back')}
          </Button>
        }
      >
        <Tabs items={tabItems} onChange={handleTabChange} />
      </Card>
    </div>
  );
}
