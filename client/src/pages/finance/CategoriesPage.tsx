import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, Modal, Popconfirm, Row, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { expenseCategoriesApi, incomeCategoriesApi, paymentMethodsApi } from '../../api/finance';
import { getApiError } from '../../api/farmApi';
import type { ExpenseCategory, IncomeCategory, PaymentMethod } from '../../types';
import { useTranslation } from 'react-i18next';

interface LookupFormValues {
  name: string;
  description?: string;
}

interface LookupRecord {
  id: string;
  name: string;
  description?: string;
  usageCount: number;
}

const CategoriesPage: React.FC = () => {const { t } = useTranslation('finance'); 
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [incomeCats, setIncomeCats] = useState<IncomeCategory[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [loading, setLoading] = useState(false);

  const [categoryModal, setCategoryModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<ExpenseCategory | null>(null);
  const [incomeCatModal, setIncomeCatModal] = useState(false);
  const [editingIncomeCat, setEditingIncomeCat] = useState<IncomeCategory | null>(null);
  const [methodModal, setMethodModal] = useState(false);
  const [editingMethod, setEditingMethod] = useState<PaymentMethod | null>(null);
  const [categoryForm] = Form.useForm();
  const [incomeCatForm] = Form.useForm();
  const [methodForm] = Form.useForm();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [catRes, incomeCatRes, methodRes] = await Promise.all([
        expenseCategoriesApi.list(),
        incomeCategoriesApi.list(),
        paymentMethodsApi.list(),
      ]);
      setCategories(catRes.data);
      setIncomeCats(incomeCatRes.data);
      setMethods(methodRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCategoryModal = (record?: ExpenseCategory) => {
    categoryForm.resetFields();
    if (record) {
      setEditingCategory(record);
      categoryForm.setFieldsValue({ name: record.name, description: record.description });
    } else {
      setEditingCategory(null);
    }
    setCategoryModal(true);
  };

  const openIncomeCatModal = (record?: IncomeCategory) => {
    incomeCatForm.resetFields();
    if (record) {
      setEditingIncomeCat(record);
      incomeCatForm.setFieldsValue({ name: record.name, description: record.description });
    } else {
      setEditingIncomeCat(null);
    }
    setIncomeCatModal(true);
  };

  const openMethodModal = (record?: PaymentMethod) => {
    methodForm.resetFields();
    if (record) {
      setEditingMethod(record);
      methodForm.setFieldsValue({ name: record.name, description: record.description });
    } else {
      setEditingMethod(null);
    }
    setMethodModal(true);
  };

  const handleSaveCategory = async () => {
    try {
      const values = (await categoryForm.validateFields()) as LookupFormValues;
      if (editingCategory) {
        await expenseCategoriesApi.update(editingCategory.id, values);
        message.success(t('categoryUpdated'));
      } else {
        await expenseCategoriesApi.create(values);
        message.success(t('categoryCreated'));
      }
      setCategoryModal(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleSaveIncomeCat = async () => {
    try {
      const values = (await incomeCatForm.validateFields()) as LookupFormValues;
      if (editingIncomeCat) {
        await incomeCategoriesApi.update(editingIncomeCat.id, values);
        message.success(t('incomeCategoryUpdated'));
      } else {
        await incomeCategoriesApi.create(values);
        message.success(t('incomeCategoryCreated'));
      }
      setIncomeCatModal(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleSaveMethod = async () => {
    try {
      const values = (await methodForm.validateFields()) as LookupFormValues;
      if (editingMethod) {
        await paymentMethodsApi.update(editingMethod.id, values);
        message.success(t('paymentMethodUpdated'));
      } else {
        await paymentMethodsApi.create(values);
        message.success(t('paymentMethodCreated'));
      }
      setMethodModal(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDeleteCategory = async (id: string) => {
    try {
      await expenseCategoriesApi.remove(id);
      message.success(t('categoryDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleDeleteIncomeCat = async (id: string) => {
    try {
      await incomeCategoriesApi.remove(id);
      message.success(t('incomeCategoryDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleDeleteMethod = async (id: string) => {
    try {
      await paymentMethodsApi.remove(id);
      message.success(t('paymentMethodDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const toRecords = <T extends { id: string; name: string; description?: string; expenseCount?: number; incomeRecordCount?: number }>(
    rows: T[],
    countKey: 'expenseCount' | 'incomeRecordCount'
  ): LookupRecord[] =>
    rows.map((r) => ({
      id: r.id,
      name: r.name,
      description: r.description,
      usageCount: r[countKey] ?? 0,
    }));

  const columns = (
    onDelete: (id: string) => void,
    onEdit: (id: string) => void,
    countLabel: string
  ): ColumnsType<LookupRecord> => [
    { title: t('name'), dataIndex: 'name' },
    // A description is the widest column and the least critical one, so it is the one that
    // gives way on a phone: Name + usage count + actions then fit a full-width card with
    // no horizontal scrolling. Back from 768px up, and always visible in the Edit modal.
    { title: t('description'), dataIndex: 'description', render: (d?: string) => d ?? '-', responsive: ['md'] },
    { title: countLabel, dataIndex: 'usageCount', align: 'center' },
    {
      title: '',
      width: 120,
      render: (_, r) => (
        <Space>
          <Button size="small" onClick={() => onEdit(r.id)}>
            {t('edit')}
          </Button>
          <Popconfirm title={t('deleteThisEntry')} onConfirm={() => onDelete(r.id)}>
            <Button size="small" danger disabled={r.usageCount > 0}>
              {t('delete')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} md={8}>
        <Card
          title={t('expenseCategories')}
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openCategoryModal()}>
              {t('add')}
            </Button>
          }
        >
          <Table
            rowKey="id"
            columns={columns(handleDeleteCategory, (id) => {
              const record = categories.find((c) => c.id === id);
              if (record) openCategoryModal(record);
            }, 'Expenses')}
            dataSource={toRecords(categories, 'expenseCount')}
            loading={loading}
            pagination={false}
            size="small"
          />
        </Card>
      </Col>
      <Col xs={24} md={8}>
        <Card
          title={t('incomeCategories')}
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openIncomeCatModal()}>
              {t('add')}
            </Button>
          }
        >
          <Table
            rowKey="id"
            columns={columns(handleDeleteIncomeCat, (id) => {
              const record = incomeCats.find((c) => c.id === id);
              if (record) openIncomeCatModal(record);
            }, 'Records')}
            dataSource={toRecords(incomeCats, 'incomeRecordCount')}
            loading={loading}
            pagination={false}
            size="small"
          />
        </Card>
      </Col>
      <Col xs={24} md={8}>
        <Card
          title={t('paymentMethods')}
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openMethodModal()}>
              {t('add')}
            </Button>
          }
        >
          <Table
            rowKey="id"
            columns={columns(handleDeleteMethod, (id) => {
              const record = methods.find((m) => m.id === id);
              if (record) openMethodModal(record);
            }, 'Uses')}
            dataSource={toRecords(methods, 'expenseCount')}
            loading={loading}
            pagination={false}
            size="small"
          />
        </Card>
      </Col>

      <Modal
        title={editingCategory ? t('editCategory') : t('newCategory')}
        open={categoryModal}
        onOk={handleSaveCategory}
        onCancel={() => setCategoryModal(false)}
        destroyOnClose
      >
        <Form form={categoryForm} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true }]}>
            <Input maxLength={100} placeholder={t('eGFeedUtilitiesMedicineEquipment')} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingIncomeCat ? t('editIncomeCategory') : t('newIncomeCategory')}
        open={incomeCatModal}
        onOk={handleSaveIncomeCat}
        onCancel={() => setIncomeCatModal(false)}
        destroyOnClose
      >
        <Form form={incomeCatForm} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true }]}>
            <Input maxLength={100} placeholder={t('eGCropSalesLivestockSalesDairy')} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingMethod ? t('editPaymentMethod') : t('newPaymentMethod')}
        open={methodModal}
        onOk={handleSaveMethod}
        onCancel={() => setMethodModal(false)}
        destroyOnClose
      >
        <Form form={methodForm} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true }]}>
            <Input maxLength={100} placeholder={t('eGCashBankTransferMobileMoneyCheck')} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Row>
  );
};

export default CategoriesPage;
