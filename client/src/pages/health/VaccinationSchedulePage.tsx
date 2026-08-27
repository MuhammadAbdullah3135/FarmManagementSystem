import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { vaccinationSchedulesApi, vaccineTypesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { VaccinationSchedule, VaccineTypeListItem } from '../../types';

interface AnimalTypeOption {
  id: string;
  name: string;
}

const VaccinationSchedulePage: React.FC = () => {
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
        message.success('Schedule updated');
      } else {
        await vaccinationSchedulesApi.create(data);
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
      await vaccinationSchedulesApi.remove(id);
      message.success('Schedule deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<VaccinationSchedule> = [
    { title: 'Vaccine', dataIndex: 'vaccineTypeName', ellipsis: true },
    {
      title: 'Animal Type',
      dataIndex: 'animalTypeName',
      render: (n?: string) => n || <Tag>All Types</Tag>,
    },
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
        title="Vaccination Schedules"
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
          <Form.Item name="vaccineTypeId" label="Vaccine Type" rules={[{ required: true, message: 'Select vaccine type' }]}>
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="Select vaccine"
              options={vaccineTypes.map((v) => ({ value: v.id, label: v.name }))}
            />
          </Form.Item>
          <Form.Item name="animalTypeId" label="Animal Type (optional — blank = all)">
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="All animal types"
              options={animalTypes.map((a) => ({ value: a.id, label: a.name }))}
            />
          </Form.Item>
          <Form.Item name="recurrenceDays" label="Repeat Every (days)" rules={[{ required: true }]}>
            <InputNumber min={1} style={{ width: '100%' }} placeholder="e.g. 180 for 6 months" />
          </Form.Item>
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

export default VaccinationSchedulePage;
