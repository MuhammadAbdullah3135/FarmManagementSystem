import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Card, Descriptions, Tag, Tabs, Table, Button, Spin, message, Breadcrumb, Empty } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { animalsApi } from '../../api/animals';
import { weightCheckStatusApi } from '../../api/health';
import { breedingRecordsApi } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import dayjs from 'dayjs';
import type { AnimalDetail, BreedingRecord, WeightCheckStatus } from '../../types';

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

export default function AnimalDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [animal, setAnimal] = useState<AnimalDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [breedingRecords, setBreedingRecords] = useState<BreedingRecord[]>([]);
  const [breedingLoading, setBreedingLoading] = useState(false);
  const [weightStatuses, setWeightStatuses] = useState<WeightCheckStatus[]>([]);

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

  const loadWeightStatus = useCallback(async () => {
    if (!id) return;
    try {
      const res = await weightCheckStatusApi.all();
      setWeightStatuses(res.data.filter(s => s.animalId === id));
    } catch { /* Optional */ }
  }, [id]);

  const handleTabChange = (key: string) => {
    if (key === 'breeding') loadBreeding();
    if (key === 'weights') loadWeightStatus();
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (!animal) return null;

  const methodLabels: Record<number, string> = { 0: 'Natural', 1: 'AI' };
  const resultLabels: Record<number, string> = { 0: 'Pending', 1: 'Confirmed', 2: 'Failed' };
  const resultColors: Record<number, string> = { 0: 'orange', 1: 'green', 2: 'red' };

  const breedingColumns = [
    {
      title: 'Date',
      dataIndex: 'breedingDate',
      key: 'breedingDate',
      render: (text: string) => dayjs(text).format('YYYY-MM-DD'),
    },
    {
      title: 'Sire',
      key: 'sire',
      render: (_: unknown, record: BreedingRecord) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.sireId}`)}>
          {record.sireName ? `${record.sireTagNumber} - ${record.sireName}` : record.sireTagNumber}
        </a>
      ),
    },
    {
      title: 'Dam',
      key: 'dam',
      render: (_: unknown, record: BreedingRecord) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.damId}`)}>
          {record.damName ? `${record.damTagNumber} - ${record.damName}` : record.damTagNumber}
        </a>
      ),
    },
    {
      title: 'Method',
      dataIndex: 'method',
      key: 'method',
      render: (val: number) => methodLabels[val] || 'Unknown',
    },
    {
      title: 'Result',
      dataIndex: 'result',
      key: 'result',
      render: (val: number) => <Tag color={resultColors[val]}>{resultLabels[val]}</Tag>,
    },
    {
      title: 'Vet',
      dataIndex: 'vetName',
      key: 'vetName',
      render: (text: string) => text || '-',
    },
  ];

  const tabItems = [
    {
      key: 'overview',
      label: 'Overview',
      children: (
        <Descriptions bordered column={2} size="small">
          <Descriptions.Item label="Tag Number">{animal.tagNumber}</Descriptions.Item>
          <Descriptions.Item label="Name">{animal.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Type">{animal.animalTypeName}</Descriptions.Item>
          <Descriptions.Item label="Breed">{animal.breedName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Sex">{animal.sexValue}</Descriptions.Item>
          <Descriptions.Item label="Status">
            <Tag color={STATUS_COLORS[animal.statusCategory] || 'default'}>{animal.statusName}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Location">{animal.locationName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Age Category">{animal.ageCategoryName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Date of Birth">{animal.dateOfBirth ? dayjs(animal.dateOfBirth).format('YYYY-MM-DD') : '-'}</Descriptions.Item>
          <Descriptions.Item label="Acquisition Date">{animal.acquisitionDate ? dayjs(animal.acquisitionDate).format('YYYY-MM-DD') : '-'}</Descriptions.Item>
          <Descriptions.Item label="Sire">{animal.sireTagNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Dam">{animal.damTagNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Notes" span={2}>{animal.notes || '-'}</Descriptions.Item>
          <Descriptions.Item label="Weight Records">{animal.weightRecordsCount}</Descriptions.Item>
          <Descriptions.Item label="Images">{animal.imagesCount}</Descriptions.Item>
          <Descriptions.Item label="Created">{dayjs(animal.createdAt).format('YYYY-MM-DD HH:mm')}</Descriptions.Item>
        </Descriptions>
      ),
    },
    {
      key: 'weights',
      label: 'Weights',
      children: (
        <div>
          {weightStatuses.length > 0 && (
            <div style={{ marginBottom: 16 }}>
              <strong>Weight Check Status:</strong>
              <div style={{ marginTop: 8 }}>
                {weightStatuses.map((ws, i) => (
                  <Tag
                    key={i}
                    color={ws.status === 'Overdue' ? 'red' : ws.status === 'Due' ? 'orange' : 'blue'}
                    style={{ marginBottom: 4 }}
                  >
                    {ws.status} — Next due: {dayjs(ws.nextDueDate).format('YYYY-MM-DD')} ({ws.daysUntilDue} days)
                  </Tag>
                ))}
              </div>
            </div>
          )}
          <Empty description={`Weight records: ${animal.weightRecordsCount}`} />
        </div>
      ),
    },
    {
      key: 'timeline',
      label: 'Timeline',
      children: (
        <Empty description="Timeline events will appear here" />
      ),
    },
    {
      key: 'breeding',
      label: 'Breeding',
      children: (
        <div>
          <div style={{ marginBottom: 16 }}>
            <Button type="primary" onClick={() => navigate('/dashboard/breeding/records')}>
              Add Breeding Record
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
          { title: <a onClick={() => navigate('/dashboard/animals')}>Animals</a> },
          { title: `${animal.tagNumber}${animal.name ? ` - ${animal.name}` : ''}` },
        ]}
      />
      <Card
        title={`${animal.tagNumber}${animal.name ? ` - ${animal.name}` : ''}`}
        extra={
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/dashboard/animals')}>
            Back
          </Button>
        }
      >
        <Tabs items={tabItems} onChange={handleTabChange} />
      </Card>
    </div>
  );
}
