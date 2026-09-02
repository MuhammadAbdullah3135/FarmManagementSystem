import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { weightCheckSchedulesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { WeightCheckSchedule } from '../../types';

interface AnimalTypeOption { id: string; name: string; }
interface BreedOption { id: string; name: string; animalTypeId: string; }
interface AgeCategoryOption { id: string; name: string; }
const PRESET_DAYS = [7, 14, 30, 45, 90];
const WeightCheckSchedulePage: React.FC = () => {
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
        message.success('Schedule updated');
      } else {
        await weightCheckSchedulesApi.create(data);
        message.success('Schedule created');
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
      message.success('Schedule deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<WeightCheckSchedule> = [
    { title: 'Animal Type', dataIndex: 'animalTypeName', render: (n?: string) => n || 'All Types' },
    { title: 'Breed', dataIndex: 'breedName', render: (n?: string) => n || 'All Breeds' },
    { title: 'Age Category', dataIndex: 'ageCategoryName', render: (n?: string) => n || 'All Ages' },
    {
      title: 'Interval',
      dataIndex: 'recurrenceDays',
      width: 120,
      render: (d: number) => {
        if (d >= 365) return `${(d / 365).toFixed(1)} year`;
        if (d >= 30) return `${Math.round(d / 30)} months`;
        return `${d} days`;
      },
    },
    {
      title: 'Active',
      dataIndex: 'isActive',
      width: 80,
      render: (a: boolean) => <Tag color={a ? 'green' : 'default'}>{a ? 'Yes' : 'No'}</Tag>,
    },
    {
      title: 'Actions',
      width: 140,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Delete this schedule?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title="Weight Check Schedules"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>Add Schedule</Button>
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
        title={editing ? 'Edit Schedule' : 'Add Schedule'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="animalTypeId" label="Animal Type (optional)">
            <Select
              allowClear showSearch optionFilterProp="label"
              placeholder="All animal types"
              onChange={(val) => { setSelectedAnimalType(val); form.setFieldsValue({ breedId: undefined }); }}
              options={animalTypes.map(a => ({ value: a.id, label: a.name }))}
            />
          </Form.Item>
          <Form.Item name="breedId" label="Breed (optional)">
            <Select
              allowClear showSearch optionFilterProp="label"
              placeholder="All breeds"
              options={filteredBreeds.map(b => ({ value: b.id, label: b.name }))}
            />
          </Form.Item>
          <Form.Item name="ageCategoryId" label="Age Category (optional)">
            <Select
              allowClear showSearch optionFilterProp="label"
              placeholder="All age categories"
              options={ageCategories.map(a => ({ value: a.id, label: a.name }))}
            />
          </Form.Item>
          <Form.Item name="recurrenceDays" label="Remind every ___ days" rules={[{ required: true }]}>
            <InputNumber min={1} style={{ width: '100%' }} placeholder="e.g. 7, 30, 45, 90" />
          </Form.Item>
          <div style={{ marginBottom: 16 }}>
            <span style={{ marginRight: 8, fontSize: 12, color: '#999' }}>Quick select:</span>
            <Space size={4}>
              {PRESET_DAYS.map(d => (
                <Tag key={d} style={{ cursor: 'pointer' }} onClick={() => form.setFieldsValue({ recurrenceDays: d })}>{d} days</Tag>
              ))}
            </Space>
          </div>
          <Form.Item name="isActive" label="Active" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default WeightCheckSchedulePage;
