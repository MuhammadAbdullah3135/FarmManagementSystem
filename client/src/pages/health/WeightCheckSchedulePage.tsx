import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { weightCheckSchedulesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { WeightCheckSchedule } from '../../types';
import { useTranslation } from 'react-i18next';

interface AnimalTypeOption { id: string; name: string; }
interface BreedOption { id: string; name: string; animalTypeId: string; }
interface AgeCategoryOption { id: string; name: string; }
const PRESET_DAYS = [7, 14, 30, 45, 90];
const WeightCheckSchedulePage: React.FC = () => {const { t } = useTranslation('health'); 
  const [schedules, setSchedules] = useState<WeightCheckSchedule[]>([]);
  const [animalTypes, setAnimalTypes] = useState<AnimalTypeOption[]>([]);
  const [breeds, setBreeds] = useState<BreedOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<AgeCategoryOption[]>([]);
  const [selectedAnimalType, setSelectedAnimalType] = useState<string | undefined>();
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<WeightCheckSchedule | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [schedRes, atRes, breedRes, ageRes] = await Promise.all([
        weightCheckSchedulesApi.list({ page: p, pageSize: 10 }),
        lookupsApi.animalTypes(),
        lookupsApi.breeds(),
        lookupsApi.ageCategories(),
      ]);
      setSchedules(schedRes.data.items);
      setTotal(schedRes.data.totalCount);
      setPage(p);
      setAnimalTypes(atRes.data);
      setBreeds(breedRes.data);
      setAgeCategories(ageRes.data);
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
      const [atRes, breedRes, ageRes] = await Promise.all([
        lookupsApi.animalTypes(),
        lookupsApi.breeds(),
        lookupsApi.ageCategories(),
      ]);
      setAnimalTypes(atRes.data);
      setBreeds(breedRes.data);
      setAgeCategories(ageRes.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ recurrenceDays: 30, isActive: true });
    setSelectedAnimalType(undefined);
    setModalOpen(true);
  };

  const filteredBreeds = breeds.filter(b => !selectedAnimalType || b.animalTypeId === selectedAnimalType);

  const openEdit = (record: WeightCheckSchedule) => {
    setEditing(record);
    form.setFieldsValue({
      animalTypeId: record.animalTypeId,
      breedId: record.breedId,
      ageCategoryId: record.ageCategoryId,
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
        animalTypeId: values.animalTypeId || undefined,
        breedId: values.breedId || undefined,
        ageCategoryId: values.ageCategoryId || undefined,
        recurrenceDays: values.recurrenceDays,
        isActive: values.isActive,
        notes: values.notes,
      };
      if (editing) {
        await weightCheckSchedulesApi.update(editing.id, data);
        message.success(t('scheduleUpdated'));
      } else {
        await weightCheckSchedulesApi.create(data);
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
      await weightCheckSchedulesApi.remove(id);
      message.success(t('scheduleDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<WeightCheckSchedule> = [
    { title: t('animalType'), dataIndex: 'animalTypeName', render: (n?: string) => n || 'All Types' },
    { title: t('breed'), dataIndex: 'breedName', render: (n?: string) => n || 'All Breeds' },
    { title: t('ageCategory'), dataIndex: 'ageCategoryName', render: (n?: string) => n || 'All Ages' },
    {
      title: t('interval'),
      dataIndex: 'recurrenceDays',
      width: 120,
      // See VaccinationSchedulePage: same phrasing, same fix for the singular.
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
        title={t('weightCheckSchedules')}
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
          <Form.Item name="animalTypeId" label={t('animalTypeOptional')}>
            <LookupQuickAddSelect
              kind="animalType"
              allowClear
              placeholder={t('allAnimalTypes')}
              options={animalTypes.map(a => ({ value: a.id, label: a.name }))}
              onValueSelected={(val) => { setSelectedAnimalType(val); form.setFieldsValue({ breedId: undefined }); }}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="breedId" label={t('breedOptional')}>
            <LookupQuickAddSelect
              kind="breed"
              ctx={{ animalTypeId: selectedAnimalType }}
              allowClear
              placeholder={t('allBreeds')}
              options={filteredBreeds.map(b => ({ value: b.id, label: b.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="ageCategoryId" label={t('ageCategoryOptional')}>
            <LookupQuickAddSelect
              kind="ageCategory"
              allowClear
              placeholder={t('allAgeCategories')}
              options={ageCategories.map(a => ({ value: a.id, label: a.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="recurrenceDays" label={t('remindEveryDays')} rules={[{ required: true }]}>
            <InputNumber min={1} style={{ width: '100%' }} placeholder={t('eG7304590')} />
          </Form.Item>
          <div style={{ marginBottom: 16 }}>
            <span style={{ marginInlineEnd: 8, fontSize: 12, color: '#999' }}>{t('quickSelect')}</span>
            <Space size={4}>
              {PRESET_DAYS.map(d => (
                <Tag key={d} style={{ cursor: 'pointer' }} onClick={() => form.setFieldsValue({ recurrenceDays: d })}>{d} {t('days')}</Tag>
              ))}
            </Space>
          </div>
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

export default WeightCheckSchedulePage;
