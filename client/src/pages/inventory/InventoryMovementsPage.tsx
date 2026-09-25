import React, { useCallback, useEffect, useState } from 'react';
import { Card, DatePicker, Select, Space, Table, Tag, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { type Dayjs } from 'dayjs';
import { inventoryApi } from '../../api/inventory';
import { getApiError } from '../../api/farmApi';
import type { InventoryItem, StockMovement } from '../../types';
import { useTranslation } from 'react-i18next';

const InventoryMovementsPage: React.FC = () => {const { t } = useTranslation('inventory'); 
  const [movements, setMovements] = useState<StockMovement[]>([]);
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [itemId, setItemId] = useState<string>();
  const [movementType, setMovementType] = useState<string>();
  const [dates, setDates] = useState<[Dayjs, Dayjs] | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => { inventoryApi.list({ page: 1, pageSize: 100 }).then((res) => setItems(res.data.items)).catch((err) => message.error(getApiError(err))); }, 0);
    return () => window.clearTimeout(timer);
  }, []);

  const load = useCallback(async (nextPage: number) => {
    setLoading(true);
    try {
      const response = await inventoryApi.movements({ page: nextPage, pageSize: 10, inventoryItemId: itemId, movementType, from: dates?.[0].format('YYYY-MM-DD'), to: dates?.[1].format('YYYY-MM-DD') });
      setMovements(response.data.items);
      setTotal(response.data.totalCount);
      setPage(nextPage);
    } catch (err) { message.error(getApiError(err)); } finally { setLoading(false); }
  }, [dates, itemId, movementType]);

  useEffect(() => { const timer = window.setTimeout(() => { void load(1); }, 0); return () => window.clearTimeout(timer); }, [load]);

  const columns: ColumnsType<StockMovement> = [
    { title: t('date'), dataIndex: 'movementDate', width: 150, render: (value: string) => dayjs(value).format('YYYY-MM-DD HH:mm') },
    { title: t('item'), dataIndex: 'inventoryItemName' },
    { title: t('type'), dataIndex: 'movementTypeName', width: 130, render: (value: string) => <Tag color={value === 'Purchase' ? 'green' : value === 'Consumption' ? 'orange' : value === 'Adjustment' ? 'blue' : 'purple'}>{value}</Tag> },
    { title: t('quantity'), dataIndex: 'signedQuantity', width: 110, render: (value: number, record) => <span style={{ color: value < 0 ? '#cf1322' : '#389e0d', fontWeight: 600 }}>{value > 0 ? '+' : ''}{value} {record.unit}</span> },
    { title: t('reason'), dataIndex: 'reason', render: (value?: string) => value || '-' },
  ];

  return <Card title={t('stockMovements')} extra={<Space wrap><Select allowClear placeholder={t('allItems')} style={{ width: 180 }} value={itemId} onChange={setItemId} options={items.map((item) => ({ value: item.id, label: item.name }))} /><Select allowClear placeholder={t('allMovementTypes')} style={{ width: 170 }} value={movementType} onChange={setMovementType} options={['Purchase', 'Consumption', 'Transfer', 'Adjustment'].map((value) => ({ value, label: value }))} /><DatePicker.RangePicker value={dates} onChange={(value) => setDates(value as [Dayjs, Dayjs] | null)} /></Space>}>
    <Table rowKey="id" columns={columns} dataSource={movements} loading={loading} pagination={{ current: page, total, pageSize: 10, showSizeChanger: false, onChange: (nextPage) => void load(nextPage) }} locale={{ emptyText: 'No stock movements found.' }} />
  </Card>;
};

export default InventoryMovementsPage;
