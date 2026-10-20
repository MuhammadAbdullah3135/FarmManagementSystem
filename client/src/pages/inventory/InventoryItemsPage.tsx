import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Switch, Table, Tag, message } from 'antd';
import { PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { ColumnsType } from 'antd/es/table';
import { inventoryApi } from '../../api/inventory';
import { configurationApi, flattenLocations } from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import { formatNumber } from '../../i18n/format';
import type { InventoryCategory, InventoryItem } from '../../types';
import { useTranslation } from 'react-i18next';
import { useEnumOptions } from '../../i18n/enumOptions';
import LookupOrText from '../../components/LookupOrText';
import ReasonSelect, { type ReasonValue } from '../../components/ReasonSelect';
import EntityPicker, { type EntityOption } from '../../components/EntityPicker';
import { INVENTORY_ADJUSTMENT_REASONS, INVENTORY_UNITS } from '../../utils/inventoryOptions';

/**
 * Money in this table has no currency symbol — the column headers say what the figure is,
 * and that is what these two columns have always shown. The number is still localised, so
 * Spanish gets its decimal comma.
 */
const money = (value: number): string =>
  formatNumber(value, undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const PRESET_REASONS = new Set(INVENTORY_ADJUSTMENT_REASONS.map((reason) => reason.value));

/**
 * The form keeps `reason` as the same string the API already accepts. The preset is a
 * shortcut that writes into that field; the textarea beside it stays available for a
 * reason the list does not contain.
 */
const toReasonValue = (value?: string): ReasonValue | undefined =>
  value && PRESET_REASONS.has(value) ? { code: value } : undefined;

const fromReasonValue = (value?: ReasonValue): string | undefined => value?.code;

const InventoryItemsPage: React.FC = () => {const { t } = useTranslation(['inventory', 'validation']); 
  const movementTypeOptions = useEnumOptions('movementType');
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
  const movementReason = Form.useWatch('reason', movementForm);
  const [categories, setCategories] = useState<InventoryCategory[]>([]);
  const [categoryLoading, setCategoryLoading] = useState(false);
  const [categoryError, setCategoryError] = useState<string | null>(null);
  const [categoryModalOpen, setCategoryModalOpen] = useState(false);
  const [editingCategory, setEditingCategory] = useState<InventoryCategory | null>(null);
  const [categoryForm] = Form.useForm();
  const categoryId = Form.useWatch('categoryId', form);
  const categoryText = Form.useWatch('category', form);

  // InventoryItem.Location stores a label. Configuration already owns the tree,
  // so the flat list is a choice list without changing what gets saved.
  const loadLocationOptions = useCallback(async (): Promise<EntityOption[]> => {
    const res = await configurationApi.locations();
    return flattenLocations(res.data).map((location) => ({
      value: location.name,
      label: `${'  '.repeat(location.depth)}${location.name}`,
    }));
  }, []);

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

  const loadCategories = useCallback(async () => {
    setCategoryLoading(true);
    setCategoryError(null);
    try {
      const response = await inventoryApi.categories({ includeInactive: true });
      setCategories(response.data);
    } catch (err) {
      setCategoryError(getApiError(err));
    } finally {
      setCategoryLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadCategories(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadCategories]);

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
      categoryId: item.categoryId,
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

  const openCategoryCreate = () => {
    setEditingCategory(null);
    categoryForm.resetFields();
    categoryForm.setFieldsValue({ isActive: true });
    setCategoryModalOpen(true);
  };

  const openCategoryEdit = (category: InventoryCategory) => {
    setEditingCategory(category);
    categoryForm.setFieldsValue({
      name: category.name,
      description: category.description,
      isActive: category.isActive,
    });
    setCategoryModalOpen(true);
  };

  const saveCategory = async () => {
    try {
      const values = await categoryForm.validateFields();
      if (editingCategory) {
        await inventoryApi.updateCategory(editingCategory.id, {
          name: values.name,
          description: values.description,
          isActive: values.isActive ?? true,
        });
        message.success(t('categoryUpdated'));
      } else {
        await inventoryApi.createCategory({ name: values.name, description: values.description });
        message.success(t('categoryCreated'));
      }
      setCategoryModalOpen(false);
      await loadCategories();
      void load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const toggleCategory = async (category: InventoryCategory) => {
    try {
      await inventoryApi.updateCategory(category.id, {
        name: category.name,
        description: category.description,
        isActive: !category.isActive,
      });
      message.success(category.isActive ? t('categoryDeactivated') : t('categoryActivated'));
      await loadCategories();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const categoryOptions: EntityOption[] = categories
    .filter((category) => category.isActive || category.id === editing?.categoryId)
    .map((category) => ({ value: category.id, label: category.name }));

  const categoryColumns: ColumnsType<InventoryCategory> = [
    {
      title: t('name'), dataIndex: 'name', ellipsis: true,
      render: (name: string, category) => (
        <Space direction="vertical" size={0}>
          <span>{name}</span>
          {!category.isActive && <Tag>{t('inactive')}</Tag>}
        </Space>
      ),
    },
    { title: t('description'), dataIndex: 'description', ellipsis: true, render: (value?: string) => value || '-' },
    { title: t('itemCount'), dataIndex: 'itemCount', width: 90 },
    {
      title: t('actions'), width: 190,
      render: (_, category) => (
        <Space size={4}>
          <Button size="small" onClick={() => openCategoryEdit(category)}>{t('edit')}</Button>
          <Button size="small" onClick={() => void toggleCategory(category)}>
            {category.isActive ? t('deactivate') : t('activate')}
          </Button>
        </Space>
      ),
    },
  ];

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
      <Table rowKey="id" columns={columns} dataSource={items} loading={loading} rowClassName={(item) => item.isLowStock ? 'inventory-low-stock' : ''} pagination={{ current: page, total, pageSize: 10, onChange: (nextPage) => void load(nextPage), showSizeChanger: false }} locale={{ emptyText: t('noInventoryItemsYet') }} />
    </Card>

    <Card
      title={t('categories')}
      extra={<Button icon={<PlusOutlined />} onClick={openCategoryCreate}>{t('addCategory')}</Button>}
      style={{ marginTop: 16 }}
    >
      {categoryError && <Alert type="error" showIcon message={categoryError} closable onClose={() => setCategoryError(null)} style={{ marginBottom: 16 }} />}
      <Table
        rowKey="id"
        size="small"
        columns={categoryColumns}
        dataSource={categories}
        loading={categoryLoading}
        pagination={false}
        locale={{ emptyText: t('noCategoriesYet') }}
      />
    </Card>

    <Modal title={editing ? t('editInventoryItem') : t('addInventoryItem')} open={modalOpen} onOk={() => void save()} onCancel={() => setModalOpen(false)} destroyOnClose>
      <Form form={form} layout="vertical">
        <Form.Item name="name" label={t('name')} rules={[{ required: true, message: t('validation:common.itemNameRequired') }]}><Input maxLength={200} placeholder={t('eGFencingWire')} /></Form.Item>
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={12}>
            <Form.Item label={t('category')}>
              {/* Register both values so validateFields returns them; the visible picker
                  and text input below are controlled by these watched values. */}
              <Form.Item name="categoryId" hidden><Input /></Form.Item>
              <Form.Item name="category" hidden><Input /></Form.Item>
              <Space.Compact block>
                <EntityPicker
                  options={categoryOptions}
                  value={categoryId}
                  onChange={(id) => {
                    const selected = categories.find((category) => category.id === id);
                    form.setFieldsValue({ categoryId: id, category: selected?.name });
                  }}
                  placeholder={t('selectCategory')}
                  allowClear
                  style={{ width: '60%' }}
                />
                <Input
                  value={categoryText}
                  onChange={(event) => form.setFieldsValue({
                    category: event.target.value,
                    categoryId: undefined,
                  })}
                  maxLength={100}
                  placeholder={t('categoryTextFallback')}
                  allowClear
                  style={{ width: '40%' }}
                />
              </Space.Compact>
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="unit" label={t('unit')} rules={[{ required: true, message: t('validation:common.unitRequired') }]}>
              <LookupOrText
                options={INVENTORY_UNITS.map((unit) => ({ value: unit, label: unit }))}
                placeholder={t('unit')}
                textPlaceholder={t('rollsPieces')}
                allowClear
              />
            </Form.Item>
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
        <Form.Item name="location" label={t('location')}>
          <LookupOrText
            loadOptions={loadLocationOptions}
            placeholder={t('mainStore')}
            recentStorageKey="inventory-location"
            allowClear
          />
        </Form.Item>
      </Form>
    </Modal>

    <Modal title={movementItem ? `Adjust stock — ${movementItem.name}` : t('adjustStock')} open={movementModalOpen} onOk={() => void saveMovement()} onCancel={() => setMovementModalOpen(false)} destroyOnClose>
      <Form form={movementForm} layout="vertical">
        <Form.Item name="movementType" label={t('movementType')} rules={[{ required: true }]}>
          <Select options={movementTypeOptions} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="quantity" label={t('quantity')} extra={t('useANegativeQuantityForADownwardAdjustment')} rules={[{ required: true, message: t('validation:common.quantityRequired') }]}>
          <InputNumber precision={3} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="movementDate" label={t('date')}><DatePicker style={{ width: '100%' }} /></Form.Item>
        <Form.Item label={t('suggestedReason')}>
          <ReasonSelect
            options={INVENTORY_ADJUSTMENT_REASONS}
            value={toReasonValue(movementReason)}
            onChange={(value) => movementForm.setFieldValue('reason', fromReasonValue(value))}
          />
        </Form.Item>
        <Form.Item name="reason" label={t('reason')}>
          <Input.TextArea rows={3} maxLength={1000} />
        </Form.Item>
      </Form>
    </Modal>

    <Modal
      title={editingCategory ? t('editCategory') : t('addCategory')}
      open={categoryModalOpen}
      onOk={() => void saveCategory()}
      onCancel={() => setCategoryModalOpen(false)}
      destroyOnClose
    >
      <Form form={categoryForm} layout="vertical">
        <Form.Item
          name="name"
          label={t('name')}
          rules={[
            { required: true, message: t('validation:common.nameRequired') },
            { max: 100, message: t('validation:validation.inventoryCategoryNameMaxLength', { max: 100 }) },
          ]}
        >
          <Input maxLength={100} placeholder={t('supplies')} />
        </Form.Item>
        <Form.Item
          name="description"
          label={t('description')}
          rules={[{ max: 500, message: t('validation:validation.inventoryCategoryDescriptionMaxLength', { max: 500 }) }]}
        >
          <Input.TextArea rows={2} maxLength={500} />
        </Form.Item>
        {editingCategory && (
          <Form.Item name="isActive" label={t('active')} valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Modal>
  </>;
};

export default InventoryItemsPage;
