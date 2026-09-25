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
import { useTranslation } from 'react-i18next';

interface LookupOption {
  id: string;
  name: string;
}

const DietPlansPage: React.FC = () => {const { t: translate } = useTranslation('feed'); 
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
        message.success(translate('dietPlanUpdated'));
      } else {
        await dietPlansApi.create(values);
        message.success(translate('dietPlanCreated'));
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
      message.success(translate('dietPlanDeleted'));
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
      message.success(translate('itemAdded'));
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
      message.success(translate('itemRemoved'));
      loadPlans();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<DietPlan> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: translate('targets'),
      render: (_, record) => (
        <Space size={4} wrap>
          {record.animalTypeName && <Tag>{record.animalTypeName}</Tag>}
          {record.breedName && <Tag>{record.breedName}</Tag>}
          {record.ageCategoryName && <Tag>{record.ageCategoryName}</Tag>}
          {(record.minWeightKg || record.maxWeightKg) && (
            <Tag>
              {record.minWeightKg ?? 0}–{record.maxWeightKg ?? '∞'} {translate('kg')}
            </Tag>
          )}
          {!record.animalTypeName && !record.breedName && !record.ageCategoryName &&
            !record.minWeightKg && !record.maxWeightKg && <span>{translate('allAnimals')}</span>}
        </Space>
      ),
    },
    { title: translate('items'), dataIndex: 'items', render: (items: DietPlanItem[]) => items.length },
    { title: translate('schedules'), dataIndex: 'scheduleCount', align: 'center' },
    {
      title: translate('active'),
      dataIndex: 'isActive',
      render: (active: boolean) =>
        active ? <Tag color="green">{translate('active')}</Tag> : <Tag color="default">{translate('inactive')}</Tag>,
    },
    {
      title: translate('actions'),
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => setItemsTarget(record)}>{translate('items')}</Button>
          <Button size="small" onClick={() => openEdit(record)}>{translate('edit')}</Button>
          <Popconfirm title={translate('deleteThisDietPlan')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>{translate('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const itemColumns: ColumnsType<DietPlanItem> = [
    { title: translate('feedType'), dataIndex: 'feedTypeName' },
    {
      title: translate('quantityPerFeeding'),
      dataIndex: 'quantityPerFeeding',
      render: (v: number, r) => `${v} ${r.unitName}`,
    },
    {
      title: '',
      render: (_, record) => (
        <Popconfirm title={translate('removeThisItem')} onConfirm={() => handleRemoveItem(record.id)}>
          <Button size="small" danger>{translate('remove')}</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <Card
      title={translate('dietPlans')}
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {translate('newDietPlan')}
        </Button>
      }
    >
      <Table rowKey="id" columns={columns} dataSource={plans} loading={loading} pagination={false} />

      <Modal
        title={editing ? translate('editDietPlan') : translate('newDietPlan')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="animalTypeId" label={translate('animalType')}>
            <LookupQuickAddSelect
              kind="animalType"
              allowClear
              placeholder={translate('any')}
              options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="ageCategoryId" label={translate('ageCategory')}>
            <LookupQuickAddSelect
              kind="ageCategory"
              allowClear
              placeholder={translate('any')}
              options={ageCategories.map((c) => ({ value: c.id, label: c.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="minWeightKg" label={translate('minWeightKg')}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="maxWeightKg" label={translate('maxWeightKg')}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="isActive" label={translate('active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Form.Item name="notes" label={translate('notes')}>
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
              placeholder={translate('feedType2')}
              style={{ width: 180 }}
              options={feedTypes.map((t) => ({ value: t.id, label: `${t.name} (${t.unitName})` }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="quantityPerFeeding" rules={[{ required: true, message: 'Required' }]}>
            <InputNumber min={0.01} placeholder={translate('qty')} />
          </Form.Item>
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAddItem}>
            {translate('add')}
          </Button>
        </Form>
      </Drawer>
    </Card>
  );
};

export default DietPlansPage;
