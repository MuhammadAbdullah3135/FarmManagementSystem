import { useState, useEffect } from 'react';
import { Card, Select, Space, Tag, Spin, Empty, Typography, InputNumber, Button, message } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { lineageApi } from '../../api/breeding';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { LineageNode, LineageResponse } from '../../types';
import { formatDate } from '../../i18n/format';

import { useTranslation } from 'react-i18next';

const { Text } = Typography;

function LineageCard({ node, side }: { node: LineageNode; side?: 'sire' | 'dam' | 'child' }) {const { t } = useTranslation('breeding'); 
  const borderColors: Record<string, string> = {
    sire: '#1677ff',
    dam: '#eb2f96',
    child: '#52c41a',
  };
  const bgColor = node.isRoot ? '#fffbe6' : '#fafafa';
  const borderColor = node.isRoot ? '#faad14' : side ? borderColors[side] || '#d9d9d9' : '#d9d9d9';
  const tagColor = node.sex === 'Male' ? 'blue' : node.sex === 'Female' ? 'pink' : 'default';

  return (
    <div style={{ display: 'inline-block', verticalAlign: 'top' }}>
      <Card
        size="small"
        style={{
          width: 220,
          marginBottom: 8,
          borderLeft: `4px solid ${borderColor}`,
          background: bgColor,
          boxShadow: node.isRoot ? '0 2px 8px rgba(0,0,0,0.15)' : 'none',
        }}
      >
        <div style={{ marginBottom: 4 }}>
          <strong style={{ fontSize: 13 }}>{node.tagNumber}</strong>
          {node.name && <Text type="secondary" style={{ marginLeft: 6, fontSize: 12 }}>({node.name})</Text>}
          {node.isRoot && <Tag color="gold" style={{ marginLeft: 6 }}>{t('root')}</Tag>}
        </div>
        <div style={{ fontSize: 12, color: '#666' }}>
          <Tag color={tagColor} style={{ fontSize: 11 }}>{node.sex}</Tag>
          {node.breed && <Tag style={{ fontSize: 11 }}>{node.breed}</Tag>}
          <Tag style={{ fontSize: 11 }}>{node.status}</Tag>
        </div>
        {node.dateOfBirth && (
          <div style={{ fontSize: 11, color: '#999', marginTop: 4 }}>
            {t('dob')} {formatDate(node.dateOfBirth)}
          </div>
        )}
      </Card>
    </div>
  );
}

function AncestorTree({ node, depth }: { node: LineageNode; depth: number }) {
  if (depth <= 0 || (!node.sire && !node.dam)) return null;

  return (
    <div style={{ display: 'flex', justifyContent: 'center', gap: 40, marginBottom: 8 }}>
      {node.sire && (
        <div style={{ textAlign: 'center' }}>
          <AncestorTree node={node.sire} depth={depth - 1} />
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <div style={{ width: 2, height: 16, background: '#1677ff' }} />
            <LineageCard node={node.sire} side="sire" />
          </div>
        </div>
      )}
      {node.dam && (
        <div style={{ textAlign: 'center' }}>
          <AncestorTree node={node.dam} depth={depth - 1} />
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <div style={{ width: 2, height: 16, background: '#eb2f96' }} />
            <LineageCard node={node.dam} side="dam" />
          </div>
        </div>
      )}
    </div>
  );
}

function DescendantTree({ nodes, depth }: { nodes: LineageNode[]; depth: number }) {
  if (depth <= 0 || nodes.length === 0) return null;

  return (
    <div style={{ display: 'flex', justifyContent: 'center', gap: 16, marginTop: 8, flexWrap: 'wrap' }}>
      {nodes.map(child => (
        <div key={child.id} style={{ textAlign: 'center' }}>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <div style={{ width: 2, height: 16, background: '#52c41a' }} />
            <LineageCard node={child} side="child" />
          </div>
          <DescendantTree nodes={child.offspring} depth={depth - 1} />
        </div>
      ))}
    </div>
  );
}

export default function LineagePage() {const { t } = useTranslation('breeding'); 
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [selectedAnimalId, setSelectedAnimalId] = useState<string | null>(null);
  const [lineage, setLineage] = useState<LineageResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [ancestorDepth, setAncestorDepth] = useState(5);
  const [descendantDepth, setDescendantDepth] = useState(3);

  useEffect(() => {
    lookupsApi.animals().then(res => setAnimals(res.data.items)).catch(() => {});
  }, []);

  const handleSearch = async () => {
    if (!selectedAnimalId) {
      message.warning(t('selectAnAnimalFirst'));
      return;
    }
    setLoading(true);
    try {
      const res = await lineageApi.get(selectedAnimalId, ancestorDepth, descendantDepth);
      setLineage(res.data);
    } catch (err) {
      message.error(getApiError(err));
      setLineage(null);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Card title={t('parentageLineageView')}>
      <Space style={{ marginBottom: 24 }} wrap>
        <Select
          showSearch
          placeholder={t('selectAnimalToViewLineage')}
          optionFilterProp="label"
          style={{ width: 350 }}
          value={selectedAnimalId}
          onChange={setSelectedAnimalId}
          options={animals.map(a => ({
            value: a.id,
            label: `${a.tagNumber} - ${a.name || 'Unnamed'}`,
          }))}
        />
        <Space>
          <span>{t('ancestors')}</span>
          <InputNumber min={1} max={10} value={ancestorDepth} onChange={v => setAncestorDepth(v || 5)} />
        </Space>
        <Space>
          <span>{t('descendants')}</span>
          <InputNumber min={1} max={10} value={descendantDepth} onChange={v => setDescendantDepth(v || 3)} />
        </Space>
        <Button type="primary" icon={<SearchOutlined />} onClick={handleSearch} loading={loading}>
          {t('viewLineage')}
        </Button>
      </Space>

      {loading && (
        <div style={{ textAlign: 'center', padding: 60 }}>
          <Spin size="large" tip="Loading lineage..." />
        </div>
      )}

      {!loading && !lineage && (
        <Empty description={t('selectAnAnimalAndClickViewLineageTo')} />
      )}

      {!loading && lineage && (
        <div style={{ overflowX: 'auto', padding: '20px 0' }}>
          <div style={{ minWidth: 600, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            {/* Ancestors */}
            <AncestorTree node={lineage.root} depth={lineage.ancestorDepth} />

            {/* Root animal */}
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', margin: '16px 0' }}>
              <div style={{ width: 2, height: 20, background: '#faad14' }} />
              <LineageCard node={lineage.root} />
            </div>

            {/* Descendants */}
            <DescendantTree nodes={lineage.root.offspring} depth={lineage.descendantDepth} />

            {lineage.root.offspring.length === 0 && (
              <Text type="secondary" style={{ marginTop: 16 }}>{t('noOffspringRecorded')}</Text>
            )}
          </div>
        </div>
      )}
    </Card>
  );
}
