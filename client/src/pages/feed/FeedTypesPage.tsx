import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tabs, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { feedInventoryApi, feedTypesApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import type { FeedStock, FeedType, FeedStockMovement } from '../../types';
import { FEED_CATEGORIES, FEED_UNITS } from '../../utils/feedOptions';

const MOVEMENT_TYPES = ['Purchase', 'Adjustment'];

const categoryColor: Record<string, string> = {
  Forage: 'green',
  Concentrate: 'orange',
  Mineral: 'blue',
  Supplement: 'purple',
  Additive: 'cyan',
  Other: 'default',
};

const FeedTypesPage: React.FC = () => {
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
        message.success('Feed type updated');
      } else {
        await feedTypesApi.create(values);
        message.success('Feed type created');
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
      message.success('Feed type deleted');
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
      message.success('Stock movement recorded');
      setMovementModalOpen(false);
      loadTypes();
      loadMovements(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const typeColumns: ColumnsType<FeedType> = [
    { title: 'Name', dataIndex: 'name' },
    {
      title: 'Category',
      dataIndex: 'categoryName',
      render: (c: string) => <Tag color={categoryColor[c]}>{c}</Tag>,
    },
    { title: 'Unit', dataIndex: 'unitName' },
    { title: 'Cost/Unit', dataIndex: 'costPerUnit', align: 'right' },
    { title: 'Notes', dataIndex: 'notes', ellipsis: true },
    {
      title: 'Actions',
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>Edit</Button>
          <Button size="small" type="primary" ghost onClick={() => openMovement(record)}>Adjust Stock</Button>
          <Popconfirm title="Delete this feed type?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const stockColumns: ColumnsType<FeedStock> = [
    { title: 'Feed Type', dataIndex: 'feedTypeName' },
    { title: 'Purchased', dataIndex: 'quantityPurchased', align: 'right' },
    { title: 'Consumed', dataIndex: 'quantityConsumed', align: 'right' },
    { title: 'Adjustments', dataIndex: 'netAdjustments', align: 'right' },
    {
      title: 'Current Stock',
      dataIndex: 'currentStock',
      align: 'right',
      render: (v: number, record) => (
        <span>{v} {record.unitName}</span>
      ),
    },
    { title: 'Inventory Value', dataIndex: 'totalCost', align: 'right' },
  ];

  const movementColumns: ColumnsType<FeedStockMovement> = [
    { title: 'Date', dataIndex: 'movementDate', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    { title: 'Feed Type', dataIndex: 'feedTypeName' },
    {
      title: 'Type',
      dataIndex: 'movementTypeName',
      render: (t: string) => (
        <Tag color={t === 'Purchase' ? 'green' : t === 'Consumption' ? 'red' : 'blue'}>{t}</Tag>
      ),
    },
    { title: 'Qty', dataIndex: 'signedQuantity', align: 'right' },
    { title: 'Supplier', dataIndex: 'supplier' },
    { title: 'Notes', dataIndex: 'notes', ellipsis: true },
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
                title="Feed Types"
                extra={
                  <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                    Add Feed Type
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
              <Card title="Current Stock">
                <Table rowKey="feedTypeId" columns={stockColumns} dataSource={stock} loading={loading} pagination={false} />
              </Card>
            ),
          },
          {
            key: 'movements',
            label: 'Stock Movements',
            children: (
              <Card title="Stock Movements">
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
        title={editing ? 'Edit Feed Type' : 'Add Feed Type'}
        open={typeModalOpen}
        onOk={handleSaveType}
        onCancel={() => setTypeModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="category" label="Category" rules={[{ required: true }]}>
            <Select options={FEED_CATEGORIES.map((c) => ({ value: c, label: c }))} />
          </Form.Item>
          <Form.Item name="unit" label="Unit" rules={[{ required: true }]}>
            <Select options={FEED_UNITS.map((u) => ({ value: u, label: u }))} />
          </Form.Item>
          <Form.Item name="costPerUnit" label="Cost per Unit" rules={[{ required: true }]}>
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
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
          <Form.Item name="movementType" label="Movement Type" rules={[{ required: true }]} initialValue="Purchase">
            <Select options={MOVEMENT_TYPES.map((t) => ({ value: t, label: t }))} />
          </Form.Item>
          <Form.Item name="quantity" label="Quantity" rules={[{ required: true }]}>
            <InputNumber min={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="unitCost" label="Unit Cost (purchases)">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="supplier" label="Supplier">
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default FeedTypesPage;
