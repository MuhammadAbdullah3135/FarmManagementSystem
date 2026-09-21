import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Drawer, Form, Input, InputNumber, Modal, Popconfirm, Row, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { dietPlansApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { DietPlan, DietPlanItem, FeedType } from '../../types';

interface LookupOption {
  id: string;
  name: string;
}

const DietPlansPage: React.FC = () => {
  const [plans, setPlans] = useState<DietPlan[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<DietPlan | null>(null);
  const [itemsTarget, setItemsTarget] = useState<DietPlan | null>(null);
  const [feedTypes, setFeedTypeOptions] = useState<FeedType[]>([]);
  const [animalTypes, setAnimalTypes] = useState<LookupOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<LookupOption[]>([]);
  const [itemForm] = Form.useForm();
  const [form] = Form.useForm();

  const loadPlans = useCallback(async () => {
    setLoading(true);
    try {
      const res = await dietPlansApi.list();
      setPlans(res.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  /** Each lookup settles independently: one failing list must not blank the others. */
  const loadOptions = useCallback(async () => {
    const [ft, at, ac] = await Promise.all([
      feedTypesApi.list().catch(() => undefined),
      lookupsApi.animalTypes().catch(() => undefined),
      lookupsApi.ageCategories().catch(() => undefined),
    ]);
    if (ft) setFeedTypeOptions(ft.data);
    if (at) setAnimalTypes(at.data);
    if (ac) setAgeCategories(ac.data);
  }, []);

  useEffect(() => {
    window.setTimeout(() => { loadPlans(); void loadOptions(); }, 0);
  }, [loadPlans, loadOptions]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ isActive: true });
    setModalOpen(true);
  };

  const openEdit = (plan: DietPlan) => {
    setEditing(plan);
    form.setFieldsValue({
      name: plan.name,
      animalTypeId: plan.animalTypeId,
      breedId: plan.breedId,
      ageCategoryId: plan.ageCategoryId,
      minWeightKg: plan.minWeightKg,
      maxWeightKg: plan.maxWeightKg,
      isActive: plan.isActive,
      notes: plan.notes,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await dietPlansApi.update(editing.id, values);
        message.success('Diet plan updated');
      } else {
        await dietPlansApi.create(values);
        message.success('Diet plan created');
      }
      setModalOpen(false);
      loadPlans();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await dietPlansApi.remove(id);
      message.success('Diet plan deleted');
      loadPlans();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleAddItem = async () => {
    if (!itemsTarget) return;
    try {
      const values = await itemForm.validateFields();
      await dietPlansApi.addItem(itemsTarget.id, values);
      message.success('Item added');
      itemForm.resetFields();
      loadPlans();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleRemoveItem = async (itemId: string) => {
    if (!itemsTarget) return;
    try {
      await dietPlansApi.removeItem(itemsTarget.id, itemId);
      message.success('Item removed');
      loadPlans();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<DietPlan> = [
    { title: 'Name', dataIndex: 'name' },
    {
      title: 'Targets',
      render: (_, record) => (
        <Space size={4} wrap>
          {record.animalTypeName && <Tag>{record.animalTypeName}</Tag>}
          {record.breedName && <Tag>{record.breedName}</Tag>}
          {record.ageCategoryName && <Tag>{record.ageCategoryName}</Tag>}
          {(record.minWeightKg || record.maxWeightKg) && (
            <Tag>
              {record.minWeightKg ?? 0}–{record.maxWeightKg ?? '∞'} kg
            </Tag>
          )}
          {!record.animalTypeName && !record.breedName && !record.ageCategoryName &&
            !record.minWeightKg && !record.maxWeightKg && <span>All animals</span>}
        </Space>
      ),
    },
    { title: 'Items', dataIndex: 'items', render: (items: DietPlanItem[]) => items.length },
    { title: 'Schedules', dataIndex: 'scheduleCount', align: 'center' },
    {
      title: 'Active',
      dataIndex: 'isActive',
      render: (active: boolean) =>
        active ? <Tag color="green">Active</Tag> : <Tag color="default">Inactive</Tag>,
    },
    {
      title: 'Actions',
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => setItemsTarget(record)}>Items</Button>
          <Button size="small" onClick={() => openEdit(record)}>Edit</Button>
          <Popconfirm title="Delete this diet plan?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const itemColumns: ColumnsType<DietPlanItem> = [
    { title: 'Feed Type', dataIndex: 'feedTypeName' },
    {
      title: 'Quantity per Feeding',
      dataIndex: 'quantityPerFeeding',
      render: (v: number, r) => `${v} ${r.unitName}`,
    },
    {
      title: '',
      render: (_, record) => (
        <Popconfirm title="Remove this item?" onConfirm={() => handleRemoveItem(record.id)}>
          <Button size="small" danger>Remove</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <Card
      title="Diet Plans"
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          New Diet Plan
        </Button>
      }
    >
      <Table rowKey="id" columns={columns} dataSource={plans} loading={loading} pagination={false} />

      <Modal
        title={editing ? 'Edit Diet Plan' : 'New Diet Plan'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="animalTypeId" label="Animal Type">
            <LookupQuickAddSelect
              kind="animalType"
              allowClear
              placeholder="Any"
              options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="ageCategoryId" label="Age Category">
            <LookupQuickAddSelect
              kind="ageCategory"
              allowClear
              placeholder="Any"
              options={ageCategories.map((c) => ({ value: c.id, label: c.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="minWeightKg" label="Min Weight (kg)">
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="maxWeightKg" label="Max Weight (kg)">
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="isActive" label="Active" valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Drawer
        title={`Diet Items — ${itemsTarget?.name ?? ''}`}
        open={!!itemsTarget}
        onClose={() => setItemsTarget(null)}
        width={480}
      >
        <Table rowKey="id" columns={itemColumns} dataSource={itemsTarget?.items ?? []} pagination={false} size="small" />
        <Form form={itemForm} layout="inline" style={{ marginTop: 16 }}>
          <Form.Item name="feedTypeId" rules={[{ required: true, message: 'Required' }]}>
            <LookupQuickAddSelect
              kind="feedType"
              placeholder="Feed type"
              style={{ width: 180 }}
              options={feedTypes.map((t) => ({ value: t.id, label: `${t.name} (${t.unitName})` }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="quantityPerFeeding" rules={[{ required: true, message: 'Required' }]}>
            <InputNumber min={0.01} placeholder="Qty" />
          </Form.Item>
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAddItem}>
            Add
          </Button>
        </Form>
      </Drawer>
    </Card>
  );
};

export default DietPlansPage;
