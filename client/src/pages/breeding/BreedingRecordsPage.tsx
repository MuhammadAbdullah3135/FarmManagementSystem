import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Modal, Form, Select, DatePicker, Input, Space, Tag, message, Popconfirm, Row, Col } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { breedingRecordsApi, type BreedingRecordListFilter, type CreateBreedingRecordPayload, type UpdateBreedingRecordPayload } from '../../api/breeding';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import dayjs from 'dayjs';
import type { BreedingRecord } from '../../types';

interface AnimalOption {
  id: string;
  tagNumber: string;
  name?: string;
}

const METHOD_LABELS: Record<number, string> = { 0: 'Natural', 1: 'AI' };
const RESULT_LABELS: Record<number, string> = { 0: 'Pending', 1: 'Confirmed', 2: 'Failed' };
const RESULT_COLORS: Record<number, string> = { 0: 'orange', 1: 'green', 2: 'red' };

export default function BreedingRecordsPage() {
  const [data, setData] = useState<BreedingRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<BreedingRecord | null>(null);
  const [males, setMales] = useState<AnimalOption[]>([]);
  const [females, setFemales] = useState<AnimalOption[]>([]);
  const [form] = Form.useForm();

  const loadLookups = useCallback(async () => {
    try {
      const [animalsRes, sexRes] = await Promise.all([
        lookupsApi.animals(),
        lookupsApi.sexOptions(),
      ]);
      const allAnimals = animalsRes.data.items as unknown as AnimalOption[];

      // Simple heuristic: filter by sex value for sire/dam
      const maleSex = sexRes.data.find((s: { id: string; value: string }) => s.value.toLowerCase().includes('male') || s.value.toLowerCase() === 'm');
      const femaleSex = sexRes.data.find((s: { id: string; value: string }) => s.value.toLowerCase().includes('female') || s.value.toLowerCase() === 'f');

      if (maleSex) {
        // We can't filter server-side by sex here, so show all and let user pick
        setMales(allAnimals);
      } else {
        setMales(allAnimals);
      }
      if (femaleSex) {
        setFemales(allAnimals);
      } else {
        setFemales(allAnimals);
      }
    } catch {}
  }, []);

  const load = useCallback(async (p = page, filters?: BreedingRecordListFilter) => {
    setLoading(true);
    try {
      const params: BreedingRecordListFilter = { page: p, pageSize: 10, ...filters };
      const res = await breedingRecordsApi.list(params);
      setData(res.data.items);
      setTotal(res.data.totalCount);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadLookups(); void load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadLookups, load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEdit = (record: BreedingRecord) => {
    setEditing(record);
    form.resetFields();
    form.setFieldsValue({
      sireId: record.sireId,
      damId: record.damId,
      breedingDate: dayjs(record.breedingDate),
      method: record.method,
      vetName: record.vetName,
      result: record.result,
      notes: record.notes,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const breedingDate = values.breedingDate.toISOString();

      if (editing) {
        const payload: UpdateBreedingRecordPayload = {
          sireId: values.sireId,
          damId: values.damId,
          breedingDate,
          method: values.method,
          vetName: values.vetName,
          result: values.result,
          notes: values.notes,
        };
        await breedingRecordsApi.update(editing.id, payload);
        message.success('Breeding record updated');
      } else {
        const payload: CreateBreedingRecordPayload = {
          sireId: values.sireId,
          damId: values.damId,
          breedingDate,
          method: values.method,
          vetName: values.vetName,
          notes: values.notes,
        };
        await breedingRecordsApi.create(payload);
        message.success('Breeding record created');
      }
      setModalOpen(false);
      load(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await breedingRecordsApi.delete(id);
      message.success('Breeding record deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const animalLabel = (a: { tagNumber: string; name?: string }) =>
    a.name ? `${a.tagNumber} - ${a.name}` : a.tagNumber;

  const columns: ColumnsType<BreedingRecord> = [
    {
      title: 'Date',
      dataIndex: 'breedingDate',
      key: 'breedingDate',
      render: (text: string) => dayjs(text).format('YYYY-MM-DD'),
    },
    {
      title: 'Sire',
      key: 'sire',
      render: (_, record) => animalLabel({ tagNumber: record.sireTagNumber, name: record.sireName }),
    },
    {
      title: 'Dam',
      key: 'dam',
      render: (_, record) => animalLabel({ tagNumber: record.damTagNumber, name: record.damName }),
    },
    {
      title: 'Method',
      dataIndex: 'method',
      key: 'method',
      render: (val: number) => METHOD_LABELS[val] || 'Unknown',
    },
    {
      title: 'Result',
      dataIndex: 'result',
      key: 'result',
      render: (val: number) => <Tag color={RESULT_COLORS[val]}>{RESULT_LABELS[val]}</Tag>,
    },
    {
      title: 'Vet',
      dataIndex: 'vetName',
      key: 'vetName',
      render: (text: string) => text || '-',
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 100,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          <Popconfirm title="Delete this record?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title="Breeding Records"
        extra={<Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>Add Record</Button>}
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: (p) => { setPage(p); load(p); } }}
        />
      </Card>

      <Modal
        title={editing ? 'Edit Breeding Record' : 'Add Breeding Record'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        width={600}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sireId" label="Sire (Male)" rules={[{ required: true }]}>
                <Select
                  options={males.map(a => ({ value: a.id, label: animalLabel(a) }))}
                  showSearch
                  optionFilterProp="label"
                  placeholder="Select sire"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="damId" label="Dam (Female)" rules={[{ required: true }]}>
                <Select
                  options={females.map(a => ({ value: a.id, label: animalLabel(a) }))}
                  showSearch
                  optionFilterProp="label"
                  placeholder="Select dam"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="breedingDate" label="Breeding Date" rules={[{ required: true }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="method" label="Method" rules={[{ required: true }]}>
                <Select
                  options={[
                    { value: 0, label: 'Natural' },
                    { value: 1, label: 'Artificial Insemination' },
                  ]}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="result" label="Result" rules={[{ required: true }]}>
              <Select
                options={[
                  { value: 0, label: 'Pending' },
                  { value: 1, label: 'Confirmed' },
                  { value: 2, label: 'Failed' },
                ]}
              />
            </Form.Item>
          )}
          <Form.Item name="vetName" label="Veterinarian">
            <Input maxLength={200} placeholder="Vet name" />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
