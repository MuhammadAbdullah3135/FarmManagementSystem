import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, Modal, Popconfirm, Row, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { expenseCategoriesApi, incomeCategoriesApi, paymentMethodsApi } from '../../api/finance';
import { getApiError } from '../../api/farmApi';
import type { ExpenseCategory, IncomeCategory, PaymentMethod } from '../../types';

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

const CategoriesPage: React.FC = () => {
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
        message.success('Category updated');
      } else {
        await expenseCategoriesApi.create(values);
        message.success('Category created');
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
        message.success('Income category updated');
      } else {
        await incomeCategoriesApi.create(values);
        message.success('Income category created');
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
        message.success('Payment method updated');
      } else {
        await paymentMethodsApi.create(values);
        message.success('Payment method created');
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
      message.success('Category deleted');
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleDeleteIncomeCat = async (id: string) => {
    try {
      await incomeCategoriesApi.remove(id);
      message.success('Income category deleted');
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleDeleteMethod = async (id: string) => {
    try {
      await paymentMethodsApi.remove(id);
      message.success('Payment method deleted');
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
    { title: 'Name', dataIndex: 'name' },
    { title: 'Description', dataIndex: 'description', render: (d?: string) => d ?? '-' },
    { title: countLabel, dataIndex: 'usageCount', align: 'center' },
    {
      title: '',
      width: 160,
      render: (_, r) => (
        <Space>
          <Button size="small" onClick={() => onEdit(r.id)}>
            Edit
          </Button>
          <Popconfirm title="Delete this entry?" onConfirm={() => onDelete(r.id)}>
            <Button size="small" danger disabled={r.usageCount > 0}>
              Delete
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Row gutter={16}>
      <Col xs={24} xl={8}>
        <Card
          title="Expense Categories"
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openCategoryModal()}>
              Add
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
      <Col xs={24} xl={8}>
        <Card
          title="Income Categories"
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openIncomeCatModal()}>
              Add
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
      <Col xs={24} xl={8}>
        <Card
          title="Payment Methods"
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => openMethodModal()}>
              Add
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
        title={editingCategory ? 'Edit Category' : 'New Category'}
        open={categoryModal}
        onOk={handleSaveCategory}
        onCancel={() => setCategoryModal(false)}
        destroyOnClose
      >
        <Form form={categoryForm} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}>
            <Input maxLength={100} placeholder="e.g. Feed, Utilities, Medicine, Equipment" />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingIncomeCat ? 'Edit Income Category' : 'New Income Category'}
        open={incomeCatModal}
        onOk={handleSaveIncomeCat}
        onCancel={() => setIncomeCatModal(false)}
        destroyOnClose
      >
        <Form form={incomeCatForm} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}>
            <Input maxLength={100} placeholder="e.g. Crop Sales, Livestock Sales, Dairy" />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingMethod ? 'Edit Payment Method' : 'New Payment Method'}
        open={methodModal}
        onOk={handleSaveMethod}
        onCancel={() => setMethodModal(false)}
        destroyOnClose
      >
        <Form form={methodForm} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}>
            <Input maxLength={100} placeholder="e.g. Cash, Bank Transfer, Mobile Money, Check" />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Row>
  );
};

export default CategoriesPage;
