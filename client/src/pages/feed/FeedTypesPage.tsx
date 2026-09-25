import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tabs, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { formatDate } from '../../i18n/format';

import { feedInventoryApi, feedTypesApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import type { FeedStock, FeedType, FeedStockMovement } from '../../types';
import { FEED_CATEGORIES, FEED_UNITS } from '../../utils/feedOptions';
import { useTranslation } from 'react-i18next';

const MOVEMENT_TYPES = ['Purchase', 'Adjustment'];

const categoryColor: Record<string, string> = {
  Forage: 'green',
  Concentrate: 'orange',
  Mineral: 'blue',
  Supplement: 'purple',
  Additive: 'cyan',
  Other: 'default',
};

const FeedTypesPage: React.FC = () => {const { t: translate } = useTranslation('feed'); 
  const [types, setTypes] = useState<FeedType[]>([]);
  const [stock, setStock] = useState<FeedStock[]>([]);
  const [movements, setMovements] = useState<FeedStockMovement[]>([]);
  const [movementsTotal, setMovementsTotal] = useState(0);
  const [movementsPage, setMovementsPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [typeModalOpen, setTypeModalOpen] = useState(false);
  const [editing, setEditing] = useState<FeedType | null>(null);
  const [movementModalOpen, setMovementModalOpen] = useState(false);
  const [movementTarget, setMovementTarget] = useState<FeedType | null>(null);
  const [form] = Form.useForm();
  const [movementForm] = Form.useForm();

  const loadTypes = useCallback(async () => {
    setLoading(true);
    try {
      const [typesRes, stockRes] = await Promise.all([
        feedTypesApi.list(),
        feedInventoryApi.stock(),
      ]);
      setTypes(typesRes.data);
      setStock(stockRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  const loadMovements = useCallback(async (page: number) => {
    try {
      const res = await feedInventoryApi.movements(undefined, page, 10);
      setMovements(res.data.items);
      setMovementsTotal(res.data.totalCount);
      setMovementsPage(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      loadTypes();
      loadMovements(1);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [loadTypes, loadMovements]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setTypeModalOpen(true);
  };

  const openEdit = (feedType: FeedType) => {
    setEditing(feedType);
    form.setFieldsValue(feedType);
    setTypeModalOpen(true);
  };

  const handleSaveType = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await feedTypesApi.update(editing.id, values);
        message.success(translate('feedTypeUpdated'));
      } else {
        await feedTypesApi.create(values);
        message.success(translate('feedTypeCreated'));
      }
      setTypeModalOpen(false);
      loadTypes();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await feedTypesApi.remove(id);
      message.success(translate('feedTypeDeleted'));
      loadTypes();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const openMovement = (feedType: FeedType) => {
    setMovementTarget(feedType);
    movementForm.resetFields();
    movementForm.setFieldsValue({ feedTypeId: feedType.id });
    setMovementModalOpen(true);
  };

  const handleRecordMovement = async () => {
    try {
      const values = await movementForm.validateFields();
      await feedInventoryApi.recordMovement({
        ...values,
        movementDate: values.movementDate ? values.movementDate.toISOString() : undefined,
      });
      message.success(translate('stockMovementRecorded'));
      setMovementModalOpen(false);
      loadTypes();
      loadMovements(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const typeColumns: ColumnsType<FeedType> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: translate('category'),
      dataIndex: 'categoryName',
      render: (c: string) => <Tag color={categoryColor[c]}>{c}</Tag>,
    },
    { title: translate('unit'), dataIndex: 'unitName' },
    { title: translate('costUnit'), dataIndex: 'costPerUnit', align: 'right' },
    { title: translate('notes'), dataIndex: 'notes', ellipsis: true },
    {
      title: translate('actions'),
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>{translate('edit')}</Button>
          <Button size="small" type="primary" ghost onClick={() => openMovement(record)}>{translate('adjustStock')}</Button>
          <Popconfirm title={translate('deleteThisFeedType')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>{translate('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const stockColumns: ColumnsType<FeedStock> = [
    { title: translate('feedType'), dataIndex: 'feedTypeName' },
    { title: translate('purchased'), dataIndex: 'quantityPurchased', align: 'right' },
    { title: translate('consumed'), dataIndex: 'quantityConsumed', align: 'right' },
    { title: translate('adjustments'), dataIndex: 'netAdjustments', align: 'right' },
    {
      title: translate('currentStock'),
      dataIndex: 'currentStock',
      align: 'right',
      render: (v: number, record) => (
        <span>{v} {record.unitName}</span>
      ),
    },
    { title: translate('inventoryValue'), dataIndex: 'totalCost', align: 'right' },
  ];

  const movementColumns: ColumnsType<FeedStockMovement> = [
    { title: translate('date'), dataIndex: 'movementDate', render: (d: string) => formatDate(d) },
    { title: translate('feedType'), dataIndex: 'feedTypeName' },
    {
      title: translate('type'),
      dataIndex: 'movementTypeName',
      render: (t: string) => (
        <Tag color={t === 'Purchase' ? 'green' : t === 'Consumption' ? 'red' : 'blue'}>{t}</Tag>
      ),
    },
    { title: translate('qty'), dataIndex: 'signedQuantity', align: 'right' },
    { title: translate('supplier'), dataIndex: 'supplier' },
    { title: translate('notes'), dataIndex: 'notes', ellipsis: true },
  ];

  return (
    <div>
      <Tabs
        items={[
          {
            key: 'types',
            label: 'Feed Types',
            children: (
              <Card
                title={translate('feedTypes')}
                extra={
                  <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                    {translate('addFeedType')}
                  </Button>
                }
              >
                <Table rowKey="id" columns={typeColumns} dataSource={types} loading={loading} pagination={false} />
              </Card>
            ),
          },
          {
            key: 'stock',
            label: 'Current Stock',
            children: (
              <Card title={translate('currentStock')}>
                <Table rowKey="feedTypeId" columns={stockColumns} dataSource={stock} loading={loading} pagination={false} />
              </Card>
            ),
          },
          {
            key: 'movements',
            label: 'Stock Movements',
            children: (
              <Card title={translate('stockMovements')}>
                <Table
                  rowKey="id"
                  columns={movementColumns}
                  dataSource={movements}
                  loading={loading}
                  pagination={{ current: movementsPage, total: movementsTotal, pageSize: 10, onChange: loadMovements }}
                />
              </Card>
            ),
          },
        ]}
      />

      <Modal
        title={editing ? translate('editFeedType') : translate('addFeedType')}
        open={typeModalOpen}
        onOk={handleSaveType}
        onCancel={() => setTypeModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="category" label={translate('category')} rules={[{ required: true }]}>
            <Select options={FEED_CATEGORIES.map((c) => ({ value: c, label: c }))} />
          </Form.Item>
          <Form.Item name="unit" label={translate('unit')} rules={[{ required: true }]}>
            <Select options={FEED_UNITS.map((u) => ({ value: u, label: u }))} />
          </Form.Item>
          <Form.Item name="costPerUnit" label={translate('costPerUnit')} rules={[{ required: true }]}>
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="notes" label={translate('notes')}>
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={`Adjust Stock — ${movementTarget?.name ?? ''}`}
        open={movementModalOpen}
        onOk={handleRecordMovement}
        onCancel={() => setMovementModalOpen(false)}
        destroyOnClose
      >
        <Form form={movementForm} layout="vertical">
          <Form.Item name="feedTypeId" hidden>
            <Input />
          </Form.Item>
          <Form.Item name="movementType" label={translate('movementType')} rules={[{ required: true }]} initialValue="Purchase">
            <Select options={MOVEMENT_TYPES.map((t) => ({ value: t, label: t }))} />
          </Form.Item>
          <Form.Item name="quantity" label={translate('quantity')} rules={[{ required: true }]}>
            <InputNumber min={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="unitCost" label={translate('unitCostPurchases')}>
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="supplier" label={translate('supplier')}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="notes" label={translate('notes')}>
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default FeedTypesPage;
