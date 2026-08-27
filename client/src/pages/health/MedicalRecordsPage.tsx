import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { medicalRecordsApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { MedicalRecordListItem, MedicalRecordStatus } from '../../types';

const STATUS_COLORS: Record<MedicalRecordStatus, string> = {
  Open: 'gold',
  InProgress: 'processing',
  Resolved: 'green',
};

interface AnimalOption {
  id: string;
  tagNumber: string;
  name?: string;
}

const MedicalRecordsPage: React.FC = () => {
  const [records, setRecords] = useState<MedicalRecordListItem[]>([]);
  const [animals, setAnimals] = useState<AnimalOption[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [filters, setFilters] = useState<{
    status?: string;
    search?: string;
  }>({});
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<MedicalRecordListItem | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [detailRecord, setDetailRecord] = useState<{
    symptoms: string;
    treatment?: string;
    medicineUsed?: string;
    dosage?: string;
    notes?: string;
  } | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [recordsRes, animalsRes] = await Promise.all([
        medicalRecordsApi.list({ ...filters, page: p, pageSize: 10 }),
        lookupsApi.animals(),
      ]);
      setRecords(recordsRes.data.items);
      setTotal(recordsRes.data.totalCount);
      setPage(p);
      setAnimals(animalsRes.data.items);
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

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ status: 'Open', dateRecorded: dayjs() });
    setModalOpen(true);
  };

  const openEdit = async (record: MedicalRecordListItem) => {
    try {
      const res = await medicalRecordsApi.get(record.id);
      const data = res.data;
      setEditing(record);
      form.setFieldsValue({
        animalId: data.animalId,
        symptoms: data.symptoms,
        diagnosis: data.diagnosis,
        treatment: data.treatment,
        medicineUsed: data.medicineUsed,
        dosage: data.dosage,
        vetName: data.vetName,
        cost: data.cost,
        dateRecorded: dayjs(data.dateRecorded),
        followUpDate: data.followUpDate ? dayjs(data.followUpDate) : null,
        status: data.status,
        notes: data.notes,
      });
      setModalOpen(true);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const openDetail = async (record: MedicalRecordListItem) => {
    try {
      const res = await medicalRecordsApi.get(record.id);
      setDetailRecord({
        symptoms: res.data.symptoms,
        treatment: res.data.treatment,
        medicineUsed: res.data.medicineUsed,
        dosage: res.data.dosage,
        notes: res.data.notes,
      });
      setDetailOpen(true);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        animalId: values.animalId,
        symptoms: values.symptoms,
        diagnosis: values.diagnosis,
        treatment: values.treatment,
        medicineUsed: values.medicineUsed,
        dosage: values.dosage,
        vetName: values.vetName,
        cost: values.cost || 0,
        dateRecorded: values.dateRecorded.format('YYYY-MM-DD'),
        followUpDate: values.followUpDate ? values.followUpDate.format('YYYY-MM-DD') : undefined,
        status: values.status,
        notes: values.notes,
      };
      if (editing) {
        await medicalRecordsApi.update(editing.id, data);
        message.success('Medical record updated');
      } else {
        await medicalRecordsApi.create(data);
        message.success('Medical record created');
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
      await medicalRecordsApi.remove(id);
      message.success('Medical record deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<MedicalRecordListItem> = [
    {
      title: 'Animal',
      render: (_, r) => (
        <span>
          {r.animalTagNumber}
          {r.animalName && <span style={{ color: '#999' }}> ({r.animalName})</span>}
        </span>
      ),
    },
    {
      title: 'Diagnosis',
      dataIndex: 'diagnosis',
      ellipsis: true,
      render: (d?: string) => d || '-',
    },
    {
      title: 'Vet',
      dataIndex: 'vetName',
      render: (v?: string) => v || '-',
    },
    {
      title: 'Cost',
      dataIndex: 'cost',
      width: 100,
      render: (c: number) => c > 0 ? `$${c.toFixed(2)}` : '-',
    },
    {
      title: 'Date',
      dataIndex: 'dateRecorded',
      width: 120,
      render: (d: string) => dayjs(d).format('YYYY-MM-DD'),
    },
    {
      title: 'Follow-up',
      dataIndex: 'followUpDate',
      width: 120,
      render: (d?: string) => d ? dayjs(d).format('YYYY-MM-DD') : '-',
    },
    {
      title: 'Status',
      dataIndex: 'statusName',
      width: 110,
      render: (name: string) => <Tag color={STATUS_COLORS[name as MedicalRecordStatus]}>{name}</Tag>,
    },
    {
      title: 'Actions',
      width: 160,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openDetail(r)}>View</Button>
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Delete this record?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title="Medical Records"
        extra={
          <Space wrap>
            <Select
              allowClear
              placeholder="Status"
              style={{ width: 130 }}
              value={filters.status}
              onChange={(v) => setFilters((f) => ({ ...f, status: v }))}
              options={['Open', 'InProgress', 'Resolved'].map((s) => ({ value: s, label: s }))}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              New Record
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
        title={editing ? 'Edit Medical Record' : 'New Medical Record'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={640}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="animalId" label="Animal" rules={[{ required: true, message: 'Select an animal' }]}>
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="Select animal"
              options={animals.map((a) => ({
                value: a.id,
                label: `${a.tagNumber}${a.name ? ` - ${a.name}` : ''}`,
              }))}
            />
          </Form.Item>
          <Form.Item name="symptoms" label="Symptoms" rules={[{ required: true, message: 'Symptoms are required' }]}>
            <Input.TextArea rows={2} maxLength={2000} placeholder="Describe symptoms..." />
          </Form.Item>
          <Form.Item name="diagnosis" label="Diagnosis">
            <Input maxLength={500} placeholder="Diagnosis" />
          </Form.Item>
          <Form.Item name="treatment" label="Treatment">
            <Input.TextArea rows={2} maxLength={1000} placeholder="Treatment plan..." />
          </Form.Item>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="medicineUsed" label="Medicine Used" style={{ flex: 1 }}>
              <Input maxLength={500} placeholder="Medicine name" />
            </Form.Item>
            <Form.Item name="dosage" label="Dosage" style={{ flex: 1 }}>
              <Input maxLength={200} placeholder="e.g. 10ml twice daily" />
            </Form.Item>
          </Space>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="vetName" label="Veterinarian" style={{ flex: 1 }}>
              <Input maxLength={200} placeholder="Vet name" />
            </Form.Item>
            <Form.Item name="cost" label="Cost ($)" style={{ flex: 1 }}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder="0.00" />
            </Form.Item>
          </Space>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="dateRecorded" label="Date Recorded" rules={[{ required: true }]} style={{ flex: 1 }}>
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="followUpDate" label="Follow-up Date" style={{ flex: 1 }}>
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Form.Item name="status" label="Status" rules={[{ required: true }]}>
            <Select options={['Open', 'InProgress', 'Resolved'].map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={2000} placeholder="Additional notes..." />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Medical Record Details"
        open={detailOpen}
        onCancel={() => setDetailOpen(false)}
        footer={null}
        width={500}
      >
        {detailRecord && (
          <div>
            <p><strong>Symptoms:</strong></p>
            <p style={{ whiteSpace: 'pre-wrap' }}>{detailRecord.symptoms}</p>
            {detailRecord.treatment && (
              <>
                <p><strong>Treatment:</strong></p>
                <p style={{ whiteSpace: 'pre-wrap' }}>{detailRecord.treatment}</p>
              </>
            )}
            {detailRecord.medicineUsed && (
              <p><strong>Medicine:</strong> {detailRecord.medicineUsed}{detailRecord.dosage ? ` — ${detailRecord.dosage}` : ''}</p>
            )}
            {detailRecord.notes && (
              <>
                <p><strong>Notes:</strong></p>
                <p style={{ whiteSpace: 'pre-wrap' }}>{detailRecord.notes}</p>
              </>
            )}
          </div>
        )}
      </Modal>
    </>
  );
};

export default MedicalRecordsPage;
