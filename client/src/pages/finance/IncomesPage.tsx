import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Tag, message } from 'antd';
import { PlusOutlined, ReloadOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import dayjs, { Dayjs } from 'dayjs';
import { incomeCategoriesApi, incomeRecordsApi, paymentMethodsApi } from '../../api/finance';
import type { IncomeRecordPayload } from '../../api/finance';
import { lookupsApi } from '../../api/attendance';
import { flattenLocations } from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { IncomeCategory, IncomeRecord, PaymentMethod } from '../../types';
import { useTranslation } from 'react-i18next';

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

const IncomesPage: React.FC = () => {const { t } = useTranslation('finance'); 
  const navigate = useNavigate();
  const actionRef = useRef<ActionType>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<IncomeRecord | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categories, setCategories] = useState<IncomeCategory[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [locationTypes, setLocationTypes] = useState<{ id: string; name: string }[]>([]);
  const [form] = Form.useForm();

  const loadOptions = useCallback(async () => {
    try {
      const [catRes, methodRes, animalRes, locationRes, locationTypeRes] = await Promise.all([
        incomeCategoriesApi.list(),
        paymentMethodsApi.list(),
        lookupsApi.animals(),
        lookupsApi.locations(),
        // Needed by the inline Add Location quick-add.
        lookupsApi.locationTypes(),
      ]);
      setCategories(catRes.data);
      setMethods(methodRes.data);
      setAnimals(animalRes.data.items);
      setLocationTypes(locationTypeRes.data);
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
        message.success(t('incomeRecordUpdated'));
      } else {
        await incomeRecordsApi.create(payload);
        message.success(t('incomeRecorded'));
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
      message.success(t('incomeRecordDeleted'));
      loadOptions();
      actionRef.current?.reload();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ProColumns<IncomeRecord>[] = [
    {
      title: t('date'),
      dataIndex: 'incomeDate',
      valueType: 'date',
      width: 110,
      search: false,
    },
    {
      title: t('dateRange'),
      dataIndex: 'incomeDateRange',
      valueType: 'dateRange',
      hideInTable: true,
      search: {
        transform: (value?: string[]) => ({ from: value?.[0], to: value?.[1] }),
      },
    },
    {
      title: t('description'),
      dataIndex: 'description',
      ellipsis: true,
      search: false,
      render: (_, record) => record.description ?? '-',
    },
    {
      title: t('search'),
      dataIndex: 'search',
      hideInTable: true,
      fieldProps: { placeholder: t('searchDescription') },
    },
    {
      title: t('category'),
      dataIndex: 'incomeCategoryId',
      width: 140,
      valueType: 'select',
      fieldProps: { options: categories.map((c) => ({ value: c.id, label: c.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => <Tag color="green">{record.incomeCategoryName}</Tag>,
    },
    {
      title: t('paymentMethod'),
      dataIndex: 'paymentMethodId',
      width: 150,
      valueType: 'select',
      fieldProps: { options: methods.map((m) => ({ value: m.id, label: m.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => record.paymentMethodName,
    },
    {
      title: t('animal'),
      dataIndex: 'animalTagNumber',
      search: false,
      width: 130,
      render: (_, record) =>
        record.animalTagNumber ? `${record.animalTagNumber}${record.animalName ? ` (${record.animalName})` : ''}` : '-',
    },
    {
      title: t('animalFilter'),
      dataIndex: 'animalId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: animals.map((a) => ({ value: a.id, label: a.name ? `${a.tagNumber} (${a.name})` : a.tagNumber })), showSearch: true, optionFilterProp: 'label' },
    },
    {
      title: t('location'),
      dataIndex: 'locationName',
      search: false,
      width: 120,
      render: (_, record) => record.locationName ?? '-',
    },
    {
      title: t('locationFilter'),
      dataIndex: 'locationId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: locations.map((l) => ({ value: l.id, label: l.name })) },
    },
    {
      title: t('amount'),
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
            {t('edit')}
          </Button>
          <Popconfirm title={t('deleteThisIncomeRecord')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>
              {t('delete')}
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
          <Button key="import" icon={<UploadOutlined />} onClick={() => navigate('/dashboard/finance/income-records/import')}>
            {t('import')}
          </Button>,
          <Button key="add" type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('addIncome')}
          </Button>,
        ]}
      />

      <Modal
        title={editing ? t('editIncome') : t('addIncome')}
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
              <Form.Item name="incomeDate" label={t('date')} rules={[{ required: true, message: 'Date is required' }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="amount" label={t('amount')} rules={[{ required: true, message: 'Amount is required' }]}>
                <InputNumber min={0.01} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="incomeCategoryId" label={t('category')} rules={[{ required: true, message: 'Category is required' }]}>
            <LookupQuickAddSelect
              kind="incomeCategory"
              placeholder={t('selectCategory')}
              options={categories.map((c) => ({ value: c.id, label: c.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="paymentMethodId" label={t('paymentMethod')} rules={[{ required: true, message: 'Payment method is required' }]}>
            <LookupQuickAddSelect
              kind="paymentMethod"
              placeholder={t('selectPaymentMethod')}
              options={methods.map((m) => ({ value: m.id, label: m.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="animalId" label={t('animalOptional')}>
                <Select
                  allowClear
                  placeholder={t('linkAnAnimal')}
                  options={animals.map((a) => ({ value: a.id, label: a.name ? `${a.tagNumber} (${a.name})` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label={t('locationOptional')}>
                <LookupQuickAddSelect
                  kind="location"
                  ctx={{
                    locationTypes: locationTypes.map((lt) => ({ value: lt.id, label: lt.name })),
                    locations: locations.map((l) => ({ value: l.id, label: l.name })),
                  }}
                  allowClear
                  placeholder={t('linkALocation')}
                  options={locations.map((l) => ({ value: l.id, label: l.name }))}
                  onCreated={() => loadOptions()}
                />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item
            name="description"
            label={t('description')}
            rules={[{ max: 1000, message: 'Description cannot exceed 1000 characters' }]}
          >
            <Input.TextArea rows={3} maxLength={1000} placeholder={t('eGSold50BushelsOfWheat')} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default IncomesPage;
