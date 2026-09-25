import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { vaccinationSchedulesApi, vaccineTypesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { VaccinationSchedule, VaccineTypeListItem } from '../../types';
import { useTranslation } from 'react-i18next';

interface AnimalTypeOption {
  id: string;
  name: string;
}

const VaccinationSchedulePage: React.FC = () => {const { t } = useTranslation('health'); 
  const [schedules, setSchedules] = useState<VaccinationSchedule[]>([]);
  const [vaccineTypes, setVaccineTypes] = useState<VaccineTypeListItem[]>([]);
  const [animalTypes, setAnimalTypes] = useState<AnimalTypeOption[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<VaccinationSchedule | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [schedRes, vaxRes, atRes] = await Promise.all([
        vaccinationSchedulesApi.list({ page: p, pageSize: 10 }),
        vaccineTypesApi.list({ page: 1, pageSize: 100 }),
        lookupsApi.animalTypes(),
      ]);
      setSchedules(schedRes.data.items);
      setTotal(schedRes.data.totalCount);
      setPage(p);
      setVaccineTypes(vaxRes.data.items);
      setAnimalTypes(atRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  /** Options-only refresh: keeps the table page untouched. */
  const loadOptions = useCallback(async () => {
    try {
      const [vaxRes, atRes] = await Promise.all([
        vaccineTypesApi.list({ page: 1, pageSize: 100 }),
        lookupsApi.animalTypes(),
      ]);
      setVaccineTypes(vaxRes.data.items);
      setAnimalTypes(atRes.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ recurrenceDays: 180, isActive: true });
    setModalOpen(true);
  };

  const openEdit = (record: VaccinationSchedule) => {
    setEditing(record);
    form.setFieldsValue({
      vaccineTypeId: record.vaccineTypeId,
      animalTypeId: record.animalTypeId,
      recurrenceDays: record.recurrenceDays,
      isActive: record.isActive,
      notes: record.notes,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        vaccineTypeId: values.vaccineTypeId,
        animalTypeId: values.animalTypeId || undefined,
        recurrenceDays: values.recurrenceDays,
        isActive: values.isActive,
        notes: values.notes,
      };
      if (editing) {
        await vaccinationSchedulesApi.update(editing.id, data);
        message.success(t('scheduleUpdated'));
      } else {
        await vaccinationSchedulesApi.create(data);
        message.success(t('scheduleCreated'));
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
      await vaccinationSchedulesApi.remove(id);
      message.success(t('scheduleDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<VaccinationSchedule> = [
    { title: t('vaccine'), dataIndex: 'vaccineTypeName', ellipsis: true },
    {
      title: t('animalType'),
      dataIndex: 'animalTypeName',
      render: (n?: string) => n || <Tag>{t('allTypes')}</Tag>,
    },
    {
      title: t('interval'),
      dataIndex: 'recurrenceDays',
      width: 120,
      // Counted in whole units for readability, and phrased by the reader's language
      // (which also fixes the singular that used to read "1 year" and "1 days").
      render: (d: number) => {
        if (d >= 365) return t('intervalYears', { count: Number((d / 365).toFixed(1)) });
        if (d >= 30) return t('intervalMonths', { count: Math.round(d / 30) });
        return t('intervalDays', { count: d });
      },
    },
    {
      title: t('active'),
      dataIndex: 'isActive',
      width: 80,
      render: (a: boolean) => <Tag color={a ? 'green' : 'default'}>{a ? t('yes') : t('no')}</Tag>,
    },
    {
      title: t('actions'),
      width: 140,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openEdit(r)}>{t('edit')}</Button>
          <Popconfirm title={t('deleteThisSchedule')} onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>{t('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title={t('vaccinationSchedules')}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('addSchedule')}</Button>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={schedules}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: load }}
        />
      </Card>

      <Modal
        title={editing ? t('editSchedule') : t('addSchedule')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="vaccineTypeId" label={t('vaccineType2')} rules={[{ required: true, message: 'Select vaccine type' }]}>
            <LookupQuickAddSelect
              kind="vaccineType"
              placeholder={t('selectVaccine')}
              options={vaccineTypes.map((v) => ({ value: v.id, label: v.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="animalTypeId" label={t('animalTypeOptionalBlankAll')}>
            <LookupQuickAddSelect
              kind="animalType"
              allowClear
              placeholder={t('allAnimalTypes')}
              options={animalTypes.map((a) => ({ value: a.id, label: a.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="recurrenceDays" label={t('repeatEveryDays')} rules={[{ required: true }]}>
            <InputNumber min={1} style={{ width: '100%' }} placeholder={t('eG180For6Months')} />
          </Form.Item>
          <Form.Item name="isActive" label={t('active')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="notes" label={t('notes')}>
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default VaccinationSchedulePage;
