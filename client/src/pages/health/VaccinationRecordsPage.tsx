import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { formatDate, formatMoney } from '../../i18n/format';
import dayjs from 'dayjs';
import { vaccinationRecordsApi, vaccineTypesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { VaccinationRecordListItem, VaccineTypeListItem } from '../../types';
import { useTranslation } from 'react-i18next';

interface AnimalOption {
  id: string;
  tagNumber: string;
  name?: string;
}

const VaccinationRecordsPage: React.FC = () => {const { t } = useTranslation('health'); 
  const [records, setRecords] = useState<VaccinationRecordListItem[]>([]);
  const [animals, setAnimals] = useState<AnimalOption[]>([]);
  const [vaccineTypes, setVaccineTypes] = useState<VaccineTypeListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [filters, setFilters] = useState<{
    vaccineTypeId?: string;
    search?: string;
  }>({});
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<VaccinationRecordListItem | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [recordsRes, animalsRes, vaxTypesRes] = await Promise.all([
        vaccinationRecordsApi.list({ ...filters, page: p, pageSize: 10 }),
        lookupsApi.animals(),
        vaccineTypesApi.list({ page: 1, pageSize: 100 }),
      ]);
      setRecords(recordsRes.data.items);
      setTotal(recordsRes.data.totalCount);
      setPage(p);
      setAnimals(animalsRes.data.items);
      setVaccineTypes(vaxTypesRes.data.items);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  /** Options-only refresh: keeps the table page and filters untouched. */
  const loadVaccineTypes = useCallback(async () => {
    try {
      const res = await vaccineTypesApi.list({ page: 1, pageSize: 100 });
      setVaccineTypes(res.data.items);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ dateGiven: dayjs(), cost: 0 });
    setModalOpen(true);
  };

  const openEdit = async (record: VaccinationRecordListItem) => {
    try {
      const res = await vaccinationRecordsApi.get(record.id);
      const data = res.data;
      setEditing(record);
      form.setFieldsValue({
        animalId: data.animalId,
        vaccineTypeId: data.vaccineTypeId,
        dateGiven: dayjs(data.dateGiven),
        vetName: data.vetName,
        batchNumber: data.batchNumber,
        cost: data.cost,
        notes: data.notes,
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
        animalId: values.animalId,
        vaccineTypeId: values.vaccineTypeId,
        dateGiven: values.dateGiven.format('YYYY-MM-DD'),
        vetName: values.vetName,
        batchNumber: values.batchNumber,
        cost: values.cost || 0,
        notes: values.notes,
      };
      if (editing) {
        await vaccinationRecordsApi.update(editing.id, data);
        message.success(t('vaccinationRecordUpdated'));
      } else {
        await vaccinationRecordsApi.create(data);
        message.success(t('vaccinationRecorded'));
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
      await vaccinationRecordsApi.remove(id);
      message.success(t('vaccinationRecordDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<VaccinationRecordListItem> = [
    {
      title: t('animal'),
      render: (_, r) => (
        <span>
          {r.animalTagNumber}
          {r.animalName && <span style={{ color: '#999' }}> ({r.animalName})</span>}
        </span>
      ),
    },
    { title: t('vaccine'), dataIndex: 'vaccineTypeName', ellipsis: true },
    {
      title: t('dateGiven'),
      dataIndex: 'dateGiven',
      width: 120,
      render: (d: string) => formatDate(d),
    },
    { title: t('vet'), dataIndex: 'vetName', render: (v?: string) => v || '-' },
    { title: t('batch'), dataIndex: 'batchNumber', render: (b?: string) => b || '-' },
    {
      title: t('cost'),
      dataIndex: 'cost',
      width: 100,
      render: (c: number) => c > 0 ? formatMoney(c) : '-',
    },
    {
      title: t('actions'),
      width: 140,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openEdit(r)}>{t('edit')}</Button>
          <Popconfirm title={t('deleteThisRecord')} onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>{t('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title={t('vaccinationRecords')}
        extra={
          <Space wrap>
            <Select
              allowClear
              placeholder={t('vaccineType')}
              style={{ width: 180 }}
              value={filters.vaccineTypeId}
              onChange={(v) => setFilters((f) => ({ ...f, vaccineTypeId: v }))}
              options={vaccineTypes.map((v) => ({ value: v.id, label: v.name }))}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              {t('recordVaccination')}
            </Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={records}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: load }}
        />
      </Card>

      <Modal
        title={editing ? t('editVaccination') : t('recordVaccination')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={520}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="animalId" label={t('animal')} rules={[{ required: true, message: 'Select an animal' }]}>
            <Select
              showSearch
              optionFilterProp="label"
              placeholder={t('selectAnimal')}
              options={animals.map((a) => ({
                value: a.id,
                label: `${a.tagNumber}${a.name ? ` - ${a.name}` : ''}`,
              }))}
            />
          </Form.Item>
          <Form.Item name="vaccineTypeId" label={t('vaccineType2')} rules={[{ required: true, message: 'Select vaccine type' }]}>
            <LookupQuickAddSelect
              kind="vaccineType"
              placeholder={t('selectVaccine')}
              options={vaccineTypes.map((v) => ({ value: v.id, label: v.name }))}
              onCreated={() => loadVaccineTypes()}
            />
          </Form.Item>
          <Form.Item name="dateGiven" label={t('dateGiven')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="vetName" label={t('veterinarian')}>
                <Input maxLength={200} placeholder={t('vetName')} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="batchNumber" label={t('batchNumber')}>
                <Input maxLength={100} placeholder={t('vaccineBatch')} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="cost" label={t('cost2')}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder="0.00" />
          </Form.Item>
          <Form.Item name="notes" label={t('notes')}>
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default VaccinationRecordsPage;
