import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Tag, message } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import dayjs, { Dayjs } from 'dayjs';
import { incomeCategoriesApi, incomeRecordsApi, paymentMethodsApi } from '../../api/finance';
import type { IncomeRecordPayload } from '../../api/finance';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { IncomeCategory, IncomeRecord, PaymentMethod } from '../../types';

interface FormValues {
  incomeDate: Dayjs;
  amount: number;
  incomeCategoryId: string;
  paymentMethodId: string;
  animalId?: string;
  locationId?: string;
  description?: string;
}

const formatAmount = (amount: number) =>
  `$${amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const IncomesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<IncomeRecord | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categories, setCategories] = useState<IncomeCategory[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [form] = Form.useForm();

  const loadOptions = useCallback(async () => {
    try {
      const [catRes, methodRes, animalRes, locationRes] = await Promise.all([
        incomeCategoriesApi.list(),
        paymentMethodsApi.list(),
        lookupsApi.animals(),
        lookupsApi.locations(),
      ]);
      setCategories(catRes.data);
      setMethods(methodRes.data);
      setAnimals(animalRes.data.items);
      setLocations(locationRes.data);
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
    form.setFieldsValue({ incomeDate: dayjs() });
    setModalOpen(true);
  };

  const openEdit = (record: IncomeRecord) => {
    setEditing(record);
    form.resetFields();
    form.setFieldsValue({
      incomeDate: dayjs(record.incomeDate),
      amount: record.amount,
      incomeCategoryId: record.incomeCategoryId,
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
      const payload: IncomeRecordPayload = {
        incomeDate: values.incomeDate.format('YYYY-MM-DD'),
        amount: values.amount,
        incomeCategoryId: values.incomeCategoryId,
        paymentMethodId: values.paymentMethodId,
        animalId: values.animalId || undefined,
        locationId: values.locationId || undefined,
        description: values.description,
      };
      if (editing) {
        await incomeRecordsApi.update(editing.id, payload);
        message.success('Income record updated');
      } else {
        await incomeRecordsApi.create(payload);
        message.success('Income recorded');
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
      await incomeRecordsApi.remove(id);
      message.success('Income record deleted');
      loadOptions();
      actionRef.current?.reload();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ProColumns<IncomeRecord>[] = [
    {
      title: 'Date',
      dataIndex: 'incomeDate',
      valueType: 'date',
      width: 110,
      search: false,
    },
    {
      title: 'Date Range',
      dataIndex: 'incomeDateRange',
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
      dataIndex: 'incomeCategoryId',
      width: 140,
      valueType: 'select',
      fieldProps: { options: categories.map((c) => ({ value: c.id, label: c.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => <Tag color="green">{record.incomeCategoryName}</Tag>,
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
      render: (_, record) => <strong style={{ color: '#52c41a' }}>{formatAmount(record.amount)}</strong>,
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
          <Popconfirm title="Delete this income record?" onConfirm={() => handleDelete(record.id)}>
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
      <ProTable<IncomeRecord>
        headerTitle="Income Records"
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        cardBordered
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          try {
            const res = await incomeRecordsApi.list({
              from: (params as { from?: string }).from,
              to: (params as { to?: string }).to,
              incomeCategoryId: params.incomeCategoryId,
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
            Add Income
          </Button>,
        ]}
      />

      <Modal
        title={editing ? 'Edit Income' : 'Add Income'}
        open={modalOpen}
        onOk={handleSubmit}
        onCancel={() => setModalOpen(false)}
        confirmLoading={submitting}
        destroyOnClose
        width={560}
      >
        <Form form={form} layout="vertical">
          <Space style={{ display: 'flex' }} size={16}>
            <Form.Item name="incomeDate" label="Date" rules={[{ required: true, message: 'Date is required' }]}>
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="amount" label="Amount" rules={[{ required: true, message: 'Amount is required' }]}>
              <InputNumber min={0.01} precision={2} prefix="$" style={{ width: 160 }} />
            </Form.Item>
          </Space>
          <Form.Item name="incomeCategoryId" label="Category" rules={[{ required: true, message: 'Category is required' }]}>
            <Select
              placeholder="Select category"
              options={categories.map((c) => ({ value: c.id, label: c.name }))}
              showSearch
              optionFilterProp="label"
            />
          </Form.Item>
          <Form.Item name="paymentMethodId" label="Payment Method" rules={[{ required: true, message: 'Payment method is required' }]}>
            <Select
              placeholder="Select payment method"
              options={methods.map((m) => ({ value: m.id, label: m.name }))}
              showSearch
              optionFilterProp="label"
            />
          </Form.Item>
          <Space style={{ display: 'flex' }} size={16}>
            <Form.Item name="animalId" label="Animal (optional)" style={{ minWidth: 240 }}>
              <Select
                allowClear
                placeholder="Link an animal"
                options={animals.map((a) => ({ value: a.id, label: a.name ? `${a.tagNumber} (${a.name})` : a.tagNumber }))}
                showSearch
                optionFilterProp="label"
              />
            </Form.Item>
            <Form.Item name="locationId" label="Location (optional)" style={{ minWidth: 240 }}>
              <Select allowClear placeholder="Link a location" options={locations.map((l) => ({ value: l.id, label: l.name }))} />
            </Form.Item>
          </Space>
          <Form.Item
            name="description"
            label="Description"
            rules={[{ max: 1000, message: 'Description cannot exceed 1000 characters' }]}
          >
            <Input.TextArea rows={3} maxLength={1000} placeholder='e.g. "Sold 50 bushels of wheat"' />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default IncomesPage;
