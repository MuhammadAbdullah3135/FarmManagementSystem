import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Space, Tag, Upload, message } from 'antd';
import Select from '../../components/Select';
import { DownloadOutlined, PaperClipOutlined, PlusOutlined, ReloadOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import dayjs, { Dayjs } from 'dayjs';
import { expensesApi, expenseCategoriesApi, expenseReceiptsApi, paymentMethodsApi } from '../../api/finance';
import type { ExpensePayload } from '../../api/finance';
import { lookupsApi } from '../../api/attendance';
import { flattenLocations } from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { Expense, ExpenseCategory, ExpenseReceipt, PaymentMethod } from '../../types';
import { useTranslation } from 'react-i18next';

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

const ExpensesPage: React.FC = () => {const { t } = useTranslation(['finance', 'validation']); 
  const navigate = useNavigate();
  const actionRef = useRef<ActionType>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Expense | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [categories, setCategories] = useState<ExpenseCategory[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [locationTypes, setLocationTypes] = useState<{ id: string; name: string }[]>([]);
  const [form] = Form.useForm();
  // Files chosen but not sent yet: a new expense has no id to attach them to until it is
  // saved, so they wait here and are uploaded straight after the save succeeds.
  const [pendingReceipts, setPendingReceipts] = useState<File[]>([]);
  const [existingReceipts, setExistingReceipts] = useState<ExpenseReceipt[]>([]);

  const loadOptions = useCallback(async () => {
    try {
      const [catRes, methodRes, animalRes, locationRes, locationTypeRes] = await Promise.all([
        expenseCategoriesApi.list(),
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
    form.setFieldsValue({ expenseDate: dayjs() });
    setPendingReceipts([]);
    setExistingReceipts([]);
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
    setPendingReceipts([]);
    setExistingReceipts(record.receipts ?? []);
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
        const saved = (await expensesApi.update(editing.id, payload)).data;
        message.success(t('expenseUpdated'));
        if (!(await saveReceipts(saved))) return;
      } else {
        const saved = (await expensesApi.create(payload)).data;
        message.success(t('expenseRecorded'));
        if (!(await saveReceipts(saved))) return;
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
      message.success(t('expenseDeleted'));
      loadOptions();
      actionRef.current?.reload();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  /**
   * Uploads the queued files for an expense that is already saved.
   *
   * Returns whether every file made it. A failure leaves the modal open on the saved
   * expense with the failed files still listed, so the work is never silently dropped.
   */
  const saveReceipts = async (expense: Expense): Promise<boolean> => {
    if (pendingReceipts.length === 0) return true;

    const failed: File[] = [];
    for (const file of pendingReceipts) {
      try {
        await expenseReceiptsApi.upload(expense.id, file);
      } catch (err) {
        failed.push(file);
        message.error(getApiError(err));
      }
    }

    setPendingReceipts(failed);
    const refreshed = (await expensesApi.get(expense.id)).data;
    setExistingReceipts(refreshed.receipts ?? []);
    setEditing(refreshed);
    actionRef.current?.reload();

    if (failed.length === 0) message.success(t('receiptUploaded'));
    return failed.length === 0;
  };

  const downloadReceipt = async (receipt: ExpenseReceipt) => {
    try {
      const res = await expenseReceiptsApi.download(receipt.expenseId, receipt.id);
      const url = URL.createObjectURL(res.data);
      const link = document.createElement('a');
      link.href = url;
      link.download = receipt.originalFileName;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const removeReceipt = async (receipt: ExpenseReceipt) => {
    try {
      await expenseReceiptsApi.remove(receipt.expenseId, receipt.id);
      setExistingReceipts((prev) => prev.filter((r) => r.id !== receipt.id));
      message.success(t('receiptDeleted'));
      actionRef.current?.reload();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ProColumns<Expense>[] = [
    {
      title: t('date'),
      dataIndex: 'expenseDate',
      valueType: 'date',
      width: 110,
      search: false,
    },
    {
      title: t('dateRange'),
      dataIndex: 'expenseDateRange',
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
      dataIndex: 'expenseCategoryId',
      width: 140,
      valueType: 'select',
      fieldProps: { options: categories.map((c) => ({ value: c.id, label: c.name })), showSearch: true, optionFilterProp: 'label' },
      render: (_, record) => <Tag color="orange">{record.expenseCategoryName}</Tag>,
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
      render: (_, record) => <strong>{formatAmount(record.amount)}</strong>,
    },
    {
      title: t('receipt'),
      dataIndex: 'receipts',
      width: 90,
      search: false,
      render: (_, record) =>
        record.receipts?.length ? (
          <Button
            type="link"
            size="small"
            icon={<PaperClipOutlined />}
            aria-label={t('receipts')}
            onClick={() => openEdit(record)}
          >
            {record.receipts.length}
          </Button>
        ) : (
          '-'
        ),
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
          <Popconfirm title={t('deleteThisExpense')} onConfirm={() => handleDelete(record.id)}>
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
          <Button key="reload" icon={<ReloadOutlined />} aria-label={t('common:refresh')}
            onClick={() => actionRef.current?.reload()} />,
          <Button key="import" icon={<UploadOutlined />} onClick={() => navigate('/dashboard/finance/expenses/import')}>
            {t('import')}
          </Button>,
          <Button key="add" type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('addExpense')}
          </Button>,
        ]}
      />

      <Modal
        title={editing ? t('editExpense') : t('addExpense')}
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
              <Form.Item name="expenseDate" label={t('date')} rules={[{ required: true, message: t('validation:common.dateRequired') }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="amount" label={t('amount')} rules={[{ required: true, message: t('validation:common.amountRequired') }]}>
                <InputNumber min={0.01} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="expenseCategoryId" label={t('category')} rules={[{ required: true, message: t('validation:common.categoryRequired') }]}>
            <LookupQuickAddSelect
              kind="expenseCategory"
              placeholder={t('selectCategory')}
              options={categories.map((c) => ({ value: c.id, label: c.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="paymentMethodId" label={t('paymentMethod')} rules={[{ required: true, message: t('validation:common.paymentMethodRequired') }]}>
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
            rules={[{ max: 1000, message: t('validation:expense.descriptionMaxLength', { max: 1000 }) }]}
          >
            <Input.TextArea rows={3} maxLength={1000} placeholder={t('eG500ForVeterinaryVisit')} />
          </Form.Item>
          <Form.Item label={t('receipts')} extra={t('receiptHelp')}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <Upload
                multiple
                showUploadList={false}
                accept="image/jpeg,image/png,image/webp,application/pdf"
                beforeUpload={(file) => {
                  setPendingReceipts((prev) => [...prev, file]);
                  return false;
                }}
              >
                <Button icon={<UploadOutlined />}>{t('addReceipt')}</Button>
              </Upload>
              {pendingReceipts.length > 0 && (
                <ul>
                  {pendingReceipts.map((file, index) => (
                    <li key={`${file.name}-${index}`}>
                      {file.name}{' '}
                      <Button
                        type="link"
                        size="small"
                        danger
                        onClick={() =>
                          setPendingReceipts((prev) => prev.filter((_, i) => i !== index))
                        }
                      >
                        {t('delete')}
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
              {existingReceipts.length > 0 && (
                <ul>
                  {existingReceipts.map((receipt) => (
                    <li key={receipt.id}>
                      <Button
                        type="link"
                        size="small"
                        icon={<DownloadOutlined />}
                        aria-label={t('download')}
                        onClick={() => void downloadReceipt(receipt)}
                      >
                        {receipt.originalFileName}
                      </Button>
                      <Popconfirm title={t('deleteThisEntry')} onConfirm={() => void removeReceipt(receipt)}>
                        <Button type="link" size="small" danger>
                          {t('delete')}
                        </Button>
                      </Popconfirm>
                    </li>
                  ))}
                </ul>
              )}
            </Space>
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default ExpensesPage;
