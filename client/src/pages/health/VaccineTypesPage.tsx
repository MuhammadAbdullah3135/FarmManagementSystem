import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, Modal, Popconfirm, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { vaccineTypesApi, medicinesApi } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { VaccineTypeListItem, MedicineListItem } from '../../types';
import { useTranslation } from 'react-i18next';

const VaccineTypesPage: React.FC = () => {const { t } = useTranslation('health'); 
  const [types, setTypes] = useState<VaccineTypeListItem[]>([]);
  const [medicines, setMedicines] = useState<MedicineListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<VaccineTypeListItem | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [typesRes, medsRes] = await Promise.all([
        vaccineTypesApi.list({ page: p, pageSize: 10, search: search || undefined }),
        medicinesApi.list({ page: 1, pageSize: 100 }),
      ]);
      setTypes(typesRes.data.items);
      setTotal(typesRes.data.totalCount);
      setPage(p);
      setMedicines(medsRes.data.items);
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

  /** Options-only refresh: keeps the table page and search untouched. */
  const loadMedicines = useCallback(async () => {
    try {
      const res = await medicinesApi.list({ page: 1, pageSize: 100 });
      setMedicines(res.data.items);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEdit = async (record: VaccineTypeListItem) => {
    try {
      const res = await vaccineTypesApi.get(record.id);
      const data = res.data;
      setEditing(record);
      form.setFieldsValue({
        name: data.name,
        defaultDosage: data.defaultDosage,
        notes: data.notes,
        linkedMedicineId: data.linkedMedicineId,
      });
      setModalOpen(true);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        name: values.name,
        defaultDosage: values.defaultDosage,
        notes: values.notes,
        linkedMedicineId: values.linkedMedicineId || undefined,
      };
      if (editing) {
        await vaccineTypesApi.update(editing.id, data);
        message.success(t('vaccineTypeUpdated'));
      } else {
        await vaccineTypesApi.create(data);
        message.success(t('vaccineTypeCreated'));
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
      await vaccineTypesApi.remove(id);
      message.success(t('vaccineTypeDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<VaccineTypeListItem> = [
    { title: t('name'), dataIndex: 'name', ellipsis: true },
    { title: t('defaultDosage'), dataIndex: 'defaultDosage', render: (d?: string) => d || '-' },
    {
      title: t('linkedMedicine'),
      dataIndex: 'linkedMedicineName',
      render: (n?: string) => n || '-',
    },
    { title: t('vaccinations'), dataIndex: 'vaccinationCount', width: 110 },
    {
      title: t('actions'),
      width: 140,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openEdit(r)}>{t('edit')}</Button>
          <Popconfirm title={t('deleteThisVaccineType')} onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>{t('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title={t('vaccineTypes')}
        extra={
          <Space>
            <Input.Search
              placeholder={t('searchVaccines')}
              allowClear
              onSearch={(v) => setSearch(v)}
              style={{ width: 220 }}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('addVaccineType')}</Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={types}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: load }}
        />
      </Card>

      <Modal
        title={editing ? t('editVaccineType') : t('addVaccineType')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true, message: 'Vaccine name is required' }]}>
            <Input maxLength={200} placeholder={t('eGFmdVaccine')} />
          </Form.Item>
          <Form.Item name="defaultDosage" label={t('defaultDosage')}>
            <Input maxLength={200} placeholder={t('eG5ml')} />
          </Form.Item>
          <Form.Item name="linkedMedicineId" label={t('linkedMedicineOptional')}>
            <LookupQuickAddSelect
              kind="medicine"
              allowClear
              placeholder={t('selectMedicineForStockTracking')}
              options={medicines.map((m) => ({ value: m.id, label: m.name }))}
              onCreated={() => loadMedicines()}
            />
          </Form.Item>
          <Form.Item name="notes" label={t('notes')}>
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default VaccineTypesPage;
