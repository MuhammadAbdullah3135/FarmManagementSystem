import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, DatePicker, Drawer, Form, Input, InputNumber, Modal, Popconfirm, Row, Space, Table, message,
} from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { formatDate, formatMoney } from '../../i18n/format';
import dayjs from 'dayjs';
import { medicinesApi } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import type { MedicineListItem, MedicineStock } from '../../types';
import { useTranslation } from 'react-i18next';

const MedicinesPage: React.FC = () => {const { t } = useTranslation('health'); 
  const [medicines, setMedicines] = useState<MedicineListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<MedicineListItem | null>(null);
  const [stockDrawer, setStockDrawer] = useState<{ open: boolean; medicine: MedicineListItem | null }>({ open: false, medicine: null });
  const [stockBatches, setStockBatches] = useState<MedicineStock[]>([]);
  const [stockLoading, setStockLoading] = useState(false);
  const [stockModalOpen, setStockModalOpen] = useState(false);
  const [form] = Form.useForm();
  const [stockForm] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await medicinesApi.list({ page: p, pageSize: 10, search: search || undefined });
      setMedicines(res.data.items);
      setTotal(res.data.totalCount);
      setPage(p);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ lowStockThreshold: 10, expiringSoonDays: 30 });
    setModalOpen(true);
  };

  const openEdit = (record: MedicineListItem) => {
    setEditing(record);
    form.setFieldsValue({
      name: record.name,
      description: record.description,
      unit: record.unit,
      lowStockThreshold: record.lowStockThreshold,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        name: values.name,
        description: values.description,
        unit: values.unit,
        lowStockThreshold: values.lowStockThreshold,
        expiringSoonDays: values.expiringSoonDays,
      };
      if (editing) {
        await medicinesApi.update(editing.id, data);
        message.success(t('medicineUpdated'));
      } else {
        await medicinesApi.create(data);
        message.success(t('medicineCreated'));
      }
      setModalOpen(false);
      load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await medicinesApi.remove(id);
      message.success(t('medicineDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const openStockDrawer = async (record: MedicineListItem) => {
    setStockDrawer({ open: true, medicine: record });
    setStockLoading(true);
    try {
      const res = await medicinesApi.getStock(record.id);
      setStockBatches(res.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setStockLoading(false);
    }
  };

  const openAddStock = () => {
    stockForm.resetFields();
    stockForm.setFieldsValue({ dateReceived: dayjs() });
    setStockModalOpen(true);
  };

  const handleAddStock = async () => {
    try {
      const values = await stockForm.validateFields();
      await medicinesApi.addStock(stockDrawer.medicine!.id, {
        batchNumber: values.batchNumber,
        quantity: values.quantity,
        unitCost: values.unitCost,
        expiryDate: values.expiryDate.format('YYYY-MM-DD'),
        supplier: values.supplier,
        dateReceived: values.dateReceived?.format('YYYY-MM-DD'),
      });
      message.success(t('stockBatchAdded'));
      setStockModalOpen(false);
      const res = await medicinesApi.getStock(stockDrawer.medicine!.id);
      setStockBatches(res.data);
      load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDeleteStock = async (stockId: string) => {
    try {
      await medicinesApi.deleteStock(stockDrawer.medicine!.id, stockId);
      message.success(t('stockBatchDeleted'));
      const res = await medicinesApi.getStock(stockDrawer.medicine!.id);
      setStockBatches(res.data);
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<MedicineListItem> = [
    { title: t('name'), dataIndex: 'name', ellipsis: true },
    { title: t('unit'), dataIndex: 'unit', width: 80 },
    {
      title: t('stock'),
      dataIndex: 'totalQuantity',
      width: 100,
      render: (qty: number, r) => (
        <span style={qty < r.lowStockThreshold ? { color: '#ff4d4f', fontWeight: 600 } : undefined}>
          {qty}
        </span>
      ),
    },
    { title: t('lowThreshold'), dataIndex: 'lowStockThreshold', width: 110 },
    { title: t('batches'), dataIndex: 'batchCount', width: 80 },
    {
      title: t('actions'),
      width: 180,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openStockDrawer(r)}>{t('stock')}</Button>
          <Button size="small" onClick={() => openEdit(r)}>{t('edit')}</Button>
          <Popconfirm title={t('deleteThisMedicine')} onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>{t('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const stockColumns: ColumnsType<MedicineStock> = [
    { title: t('batch'), dataIndex: 'batchNumber' },
    { title: t('qty'), dataIndex: 'quantity', width: 80 },
    { title: t('unitCost'), dataIndex: 'unitCost', width: 100, render: (c: number) => formatMoney(c) },
    {
      title: t('expiry'),
      dataIndex: 'expiryDate',
      width: 120,
      render: (d: string) => {
        const isExpired = dayjs(d).isBefore(dayjs(), 'day');
        const isSoon = dayjs(d).isBefore(dayjs().add(30, 'day'), 'day');
        return (
          <span style={isExpired ? { color: '#ff4d4f', fontWeight: 600 } : isSoon ? { color: '#fa8c16' } : undefined}>
            {formatDate(d)}
          </span>
        );
      },
    },
    { title: t('supplier'), dataIndex: 'supplier', render: (s?: string) => s || '-' },
    {
      title: '',
      width: 40,
      render: (_, r) => (
        <Popconfirm title={t('deleteThisBatch')} onConfirm={() => handleDeleteStock(r.id)}>
          <Button size="small" danger icon={<DeleteOutlined />} />
        </Popconfirm>
      ),
    },
  ];

  return (
    <>
      <Card
        title={t('medicineInventory')}
        extra={
          <Space>
            <Input.Search
              placeholder={t('searchMedicines')}
              allowClear
              onSearch={(v) => setSearch(v)}
              style={{ width: 220 }}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('addMedicine')}</Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={medicines}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: load }}
        />
      </Card>

      {/* Create/Edit Medicine Modal */}
      <Modal
        title={editing ? t('editMedicine') : t('addMedicine')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true, message: 'Medicine name is required' }]}>
            <Input maxLength={200} placeholder={t('eGIvermectin')} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
          <Form.Item name="unit" label={t('unit')} rules={[{ required: true, message: 'Unit is required' }]}>
            <Input maxLength={50} placeholder={t('eGDosesMlTablets')} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="lowStockThreshold" label={t('lowStockThreshold')}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="expiringSoonDays" label={t('expiringSoonDays')}>
                <InputNumber min={1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>

      {/* Stock Drawer */}
      <Drawer
        title={stockDrawer.medicine ? `${stockDrawer.medicine.name} — Stock Batches` : t('stockBatches')}
        open={stockDrawer.open}
        onClose={() => setStockDrawer({ open: false, medicine: null })}
        width={600}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openAddStock}>{t('addBatch')}</Button>
        }
      >
        <Table
          rowKey="id"
          columns={stockColumns}
          dataSource={stockBatches}
          loading={stockLoading}
          pagination={false}
          size="small"
        />
      </Drawer>

      {/* Add Stock Batch Modal */}
      <Modal
        title={t('addStockBatch')}
        open={stockModalOpen}
        onOk={handleAddStock}
        onCancel={() => setStockModalOpen(false)}
        destroyOnClose
      >
        <Form form={stockForm} layout="vertical">
          <Form.Item name="batchNumber" label={t('batchNumber')} rules={[{ required: true }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="quantity" label={t('quantity')} rules={[{ required: true }]}>
                <InputNumber min={1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="unitCost" label={t('unitCost2')} rules={[{ required: true }]}>
                <InputNumber min={0} precision={2} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="expiryDate" label={t('expiryDate')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="supplier" label={t('supplier')}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="dateReceived" label={t('dateReceived')}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default MedicinesPage;
