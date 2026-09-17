import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Tag, message } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import dayjs, { Dayjs } from 'dayjs';
import { expensesApi, expenseCategoriesApi, paymentMethodsApi } from '../../api/finance';
import type { ExpensePayload } from '../../api/finance';
import { lookupsApi } from '../../api/attendance';
import { flattenLocations } from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import type { Expense, ExpenseCategory, PaymentMethod } from '../../types';

interface FormValues {
  expenseDate: Dayjs;
  amount: number;
  expenseCategoryId: string;
  paymentMethodId: string;
  animalId?: string;
  locationId?: string;
  description?: string;
}

const formatAmount = (amount: number) =>
  `$${amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const ExpensesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Expense | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [form] = Form.useForm();

  const loadOptions = useCallback(async () => {
    try {
      const [catRes, methodRes, animalRes, locationRes] = await Promise.all([
        expenseCategoriesApi.list(),
        paymentMethodsApi.list(),
        lookupsApi.animals(),
        lookupsApi.locations(),
      ]);
      setCategories(catRes.data);
      setMethods(methodRes.data);
      setAnimals(animalRes.data.items);
      // Flatten the location tree so nested (child) locations appear too.
      setLocations(
        flattenLocations(locationRes.data).map((l) => ({
          id: l.id,
          name: `${'\u00A0\u00A0'.repeat(l.depth)}${l.name}`,
        })),
      );
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { loadOptions(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadOptions]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ expenseDate: dayjs() });
    setModalOpen(true);
  };

  const openEdit = (record: Expense) => {
    setEditing(record);
    form.resetFields();
    form.setFieldsValue({
      expenseDate: dayjs(record.expenseDate),
      amount: record.amount,
      expenseCategoryId: record.expenseCategoryId,
      paymentMethodId: record.paymentMethodId,
      animalId: record.animalId,
      locationId: record.locationId,
      description: record.description,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    try {
      const values = (await form.validateFields()) as FormValues;
      setSubmitting(true);
      const payload: ExpensePayload = {
        expenseDate: values.expenseDate.format('YYYY-MM-DD'),
        amount: values.amount,
        expenseCategoryId: values.expenseCategoryId,
        paymentMethodId: values.paymentMethodId,
        animalId: values.animalId || undefined,
        locationId: values.locationId || undefined,
        description: values.description,
      };
      if (editing) {
        await expensesApi.update(editing.id, payload);
        message.success('Expense updated');
      } else {
        await expensesApi.create(payload);
        message.success('Expense recorded');
      }
      setModalOpen(false);
      loadOptions();
      actionRef.current?.reload();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await expensesApi.remove(id);
      message.success('Expense deleted');
      loadOptions();
      actionRef.current?.reload();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ProColumns<Expense>[] = [
    {
      title: 'Date',
      dataIndex: 'expenseDate',
      valueType: 'date',
      width: 110,
      search: false,
    },
    {
      title: 'Date Range',
      dataIndex: 'expenseDateRange',
      valueType: 'dateRange',
      hideInTable: true,
      search: {
        transform: (value?: string[]) => ({ from: value?.[0], to: value?.[1] }),
      },
    },
    {
      title: 'Description',
      dataIndex: 'description',
      ellipsis: true,
      search: false,
      render: (_, record) => record.description ?? '-',
    },
    {
      title: 'Search',
      dataIndex: 'search',
      hideInTable: true,
      fieldProps: { placeholder: 'Search description' },
    },
    {
      title: 'Category',
      dataIndex: 'expenseCategoryId',
      width: 140,
      valueType: 'select',
      fieldProps: { options: categories.map((c) => ({ value: c.id, label: c.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => <Tag color="orange">{record.expenseCategoryName}</Tag>,
    },
    {
      title: 'Payment Method',
      dataIndex: 'paymentMethodId',
      width: 150,
      valueType: 'select',
      fieldProps: { options: methods.map((m) => ({ value: m.id, label: m.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => record.paymentMethodName,
    },
    {
      title: 'Animal',
      dataIndex: 'animalTagNumber',
      search: false,
      width: 130,
      render: (_, record) =>
        record.animalTagNumber ? `${record.animalTagNumber}${record.animalName ? ` (${record.animalName})` : ''}` : '-',
    },
    {
      title: 'Animal Filter',
      dataIndex: 'animalId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: animals.map((a) => ({ value: a.id, label: a.name ? `${a.tagNumber} (${a.name})` : a.tagNumber })), showSearch: true, optionFilterProp: 'label' },
    },
    {
      title: 'Location',
      dataIndex: 'locationName',
      search: false,
      width: 120,
      render: (_, record) => record.locationName ?? '-',
    },
    {
      title: 'Location Filter',
      dataIndex: 'locationId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: locations.map((l) => ({ value: l.id, label: l.name })) },
    },
    {
      title: 'Amount',
      dataIndex: 'amount',
      width: 120,
      align: 'right',
      search: false,
      render: (_, record) => <strong>{formatAmount(record.amount)}</strong>,
    },
    {
      title: '',
      valueType: 'option',
      width: 140,
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>
            Edit
          </Button>
          <Popconfirm title="Delete this expense?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>
              Delete
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <ProTable<Expense>
        headerTitle="Expenses"
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        cardBordered
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          try {
            const res = await expensesApi.list({
              from: (params as { from?: string }).from,
              to: (params as { to?: string }).to,
              expenseCategoryId: params.expenseCategoryId,
              paymentMethodId: params.paymentMethodId,
              animalId: params.animalId,
              locationId: params.locationId,
              search: params.search,
              page: params.current ?? 1,
              pageSize: params.pageSize ?? 20,
            });
            return { data: res.data.items, total: res.data.totalCount, success: true };
          } catch (err) {
            message.error(getApiError(err));
            return { data: [], total: 0, success: false };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        toolBarRender={() => [
          <Button key="reload" icon={<ReloadOutlined />} onClick={() => actionRef.current?.reload()} />,
          <Button key="add" type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Add Expense
          </Button>,
        ]}
      />

      <Modal
        title={editing ? 'Edit Expense' : 'Add Expense'}
        open={modalOpen}
        onOk={handleSubmit}
        onCancel={() => setModalOpen(false)}
        confirmLoading={submitting}
        destroyOnClose
        width={560}
      >
        <Form form={form} layout="vertical">
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="expenseDate" label="Date" rules={[{ required: true, message: 'Date is required' }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="amount" label="Amount" rules={[{ required: true, message: 'Amount is required' }]}>
                <InputNumber min={0.01} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="expenseCategoryId" label="Category" rules={[{ required: true, message: 'Category is required' }]}>
            <Select
              placeholder="Select category"
              options={categories.map((c) => ({ value: c.id, label: c.name }))}
              showSearch
              optionFilterProp="label"
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item name="paymentMethodId" label="Payment Method" rules={[{ required: true, message: 'Payment method is required' }]}>
            <Select
              placeholder="Select payment method"
              options={methods.map((m) => ({ value: m.id, label: m.name }))}
              showSearch
              optionFilterProp="label"
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="animalId" label="Animal (optional)">
                <Select
                  allowClear
                  placeholder="Link an animal"
                  options={animals.map((a) => ({ value: a.id, label: a.name ? `${a.tagNumber} (${a.name})` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label="Location (optional)">
                <Select allowClear placeholder="Link a location" options={locations.map((l) => ({ value: l.id, label: l.name }))} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item
            name="description"
            label="Description"
            rules={[{ max: 1000, message: 'Description cannot exceed 1000 characters' }]}
          >
            <Input.TextArea rows={3} maxLength={1000} placeholder='e.g. "$500 for Veterinary Visit"' />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default ExpensesPage;
