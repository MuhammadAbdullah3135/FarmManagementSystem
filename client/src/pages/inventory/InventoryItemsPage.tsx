import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Table, Tag, message } from 'antd';
import { PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { ColumnsType } from 'antd/es/table';
import { inventoryApi } from '../../api/inventory';
import { getApiError } from '../../api/farmApi';
import { formatNumber } from '../../i18n/format';
import type { InventoryItem } from '../../types';
import { useTranslation } from 'react-i18next';

/**
 * Money in this table has no currency symbol — the column headers say what the figure is,
 * and that is what these two columns have always shown. The number is still localised, so
 * Spanish gets its decimal comma.
 */
const money = (value: number): string =>
  formatNumber(value, undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const InventoryItemsPage: React.FC = () => {const { t } = useTranslation('inventory'); 
  const navigate = useNavigate();
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<InventoryItem | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm();
  const [movementModalOpen, setMovementModalOpen] = useState(false);
  const [movementItem, setMovementItem] = useState<InventoryItem | null>(null);
  const [movementForm] = Form.useForm();

  const load = useCallback(async (nextPage: number) => {
    setLoading(true);
    setError(null);
    try {
      const response = await inventoryApi.list({ page: nextPage, pageSize: 10, search: search || undefined });
      setItems(response.data.items);
      setTotal(response.data.totalCount);
      setPage(nextPage);
    } catch (err) {
      setError(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ quantity: 0, reorderLevel: 0, unitCost: 0 });
    setModalOpen(true);
  };

  const openMovement = (item: InventoryItem) => {
    setMovementItem(item);
    movementForm.resetFields();
    movementForm.setFieldsValue({ movementType: 'Adjustment' });
    setMovementModalOpen(true);
  };

  const saveMovement = async () => {
    try {
      const values = await movementForm.validateFields();
      await inventoryApi.recordMovement(movementItem!.id, values);
      message.success(t('stockMovementRecorded'));
      setMovementModalOpen(false);
      void load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const openEdit = (item: InventoryItem) => {
    setEditing(item);
    form.setFieldsValue({
      name: item.name,
      category: item.category,
      unit: item.unit,
      reorderLevel: item.reorderLevel,
      unitCost: item.unitCost,
      location: item.location,
    });
    setModalOpen(true);
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await inventoryApi.update(editing.id, values);
        message.success(t('inventoryItemUpdated'));
      } else {
        await inventoryApi.create(values);
        message.success(t('inventoryItemCreated'));
      }
      setModalOpen(false);
      void load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const remove = async (id: string) => {
    try {
      await inventoryApi.remove(id);
      message.success(t('inventoryItemDeleted'));
      void load(items.length === 1 && page > 1 ? page - 1 : page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<InventoryItem> = [
    {
      title: t('item'), dataIndex: 'name', ellipsis: true,
      render: (name: string, item) => <Space direction="vertical" size={0}><span>{name}</span>{item.category && <small style={{ color: '#888' }}>{item.category}</small>}</Space>,
    },
    { title: t('unit'), dataIndex: 'unit', width: 90 },
    {
      title: t('quantity'), dataIndex: 'quantity', width: 120,
      render: (quantity: number, item) => <Space>{quantity} {item.isLowStock && <Tag color="error">{t('lowStock')}</Tag>}</Space>,
    },
    { title: t('reorderLevel'), dataIndex: 'reorderLevel', width: 120 },
    { title: t('unitCost'), dataIndex: 'unitCost', width: 110, render: (value: number) => money(value) },
    { title: t('stockValue'), dataIndex: 'stockValue', width: 110, render: (value: number) => money(value) },
    { title: t('location'), dataIndex: 'location', width: 140, render: (value?: string) => value || '-' },
    {
      title: t('actions'), width: 145,
      render: (_, item) => <Space size={4}><Button size="small" onClick={() => openMovement(item)}>{t('adjustStock')}</Button><Button size="small" onClick={() => openEdit(item)}>{t('edit')}</Button><Popconfirm title={t('deleteThisItem')} onConfirm={() => void remove(item.id)}><Button size="small" danger>{t('delete')}</Button></Popconfirm></Space>,
    },
  ];

  return <>
    <Card title={t('inventoryItems')} extra={<Space><Input.Search placeholder={t('searchItems')} allowClear onSearch={setSearch} style={{ width: 220 }} /><Button icon={<UploadOutlined />} onClick={() => navigate('/dashboard/inventory/items/import')}>{t('import')}</Button><Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('addItem')}</Button></Space>}>
      {error && <Alert type="error" showIcon message={error} closable onClose={() => setError(null)} style={{ marginBottom: 16 }} />}
      <Table rowKey="id" columns={columns} dataSource={items} loading={loading} rowClassName={(item) => item.isLowStock ? 'inventory-low-stock' : ''} pagination={{ current: page, total, pageSize: 10, onChange: (nextPage) => void load(nextPage), showSizeChanger: false }} locale={{ emptyText: 'No inventory items yet. Add equipment, supplies, or consumables to get started.' }} />
    </Card>

    <Modal title={editing ? t('editInventoryItem') : t('addInventoryItem')} open={modalOpen} onOk={() => void save()} onCancel={() => setModalOpen(false)} destroyOnClose>
      <Form form={form} layout="vertical">
        <Form.Item name="name" label={t('name')} rules={[{ required: true, message: 'Item name is required' }]}><Input maxLength={200} placeholder={t('eGFencingWire')} /></Form.Item>
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={12}>
            <Form.Item name="category" label={t('category')}><Input maxLength={100} placeholder={t('supplies')} /></Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="unit" label={t('unit')} rules={[{ required: true, message: 'Unit is required' }]}><Input maxLength={50} placeholder={t('rollsPieces')} /></Form.Item>
          </Col>
        </Row>
        {!editing && <Form.Item name="quantity" label={t('openingQuantity')} rules={[{ required: true }]}><InputNumber min={0} precision={3} style={{ width: '100%' }} /></Form.Item>}
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={12}>
            <Form.Item name="reorderLevel" label={t('reorderLevel')} rules={[{ required: true }]}><InputNumber min={0} precision={3} style={{ width: '100%' }} /></Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="unitCost" label={t('unitCost')} rules={[{ required: true }]}><InputNumber min={0} precision={2} style={{ width: '100%' }} /></Form.Item>
          </Col>
        </Row>
        <Form.Item name="location" label={t('location')}><Input maxLength={200} placeholder={t('mainStore')} /></Form.Item>
      </Form>
    </Modal>

    <Modal title={movementItem ? `Adjust stock — ${movementItem.name}` : t('adjustStock')} open={movementModalOpen} onOk={() => void saveMovement()} onCancel={() => setMovementModalOpen(false)} destroyOnClose>
      <Form form={movementForm} layout="vertical">
        <Form.Item name="movementType" label={t('movementType')} rules={[{ required: true }]}>
          <Select options={['Purchase', 'Consumption', 'Transfer', 'Adjustment'].map((value) => ({ value, label: value }))} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="quantity" label={t('quantity')} extra={t('useANegativeQuantityForADownwardAdjustment')} rules={[{ required: true, message: 'Quantity is required' }]}>
          <InputNumber precision={3} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="movementDate" label={t('date')}><DatePicker style={{ width: '100%' }} /></Form.Item>
        <Form.Item name="reason" label={t('reason')}><Input.TextArea rows={3} maxLength={1000} /></Form.Item>
      </Form>
    </Modal>
  </>;
};

export default InventoryItemsPage;
