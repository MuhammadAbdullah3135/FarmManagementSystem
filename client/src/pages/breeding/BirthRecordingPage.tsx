import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Tag, Space, Col, Modal, Form, DatePicker, Input, InputNumber, Select, message, Descriptions, Empty, Popconfirm, Row } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, DeleteOutlined, EyeOutlined } from '@ant-design/icons';
import { birthsApi, gestationApi, breedingRecordsApi, type CreateBirthRecordPayload } from '../../api/breeding';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import dayjs from 'dayjs';
import type { BirthRecord, GestationRecord, BreedingRecord } from '../../types';

const OUTCOME_LABELS: Record<number, string> = { 0: 'Alive', 1: 'Stillborn', 2: 'Weak' };
const OUTCOME_COLORS: Record<number, string> = { 0: 'green', 1: 'red', 2: 'orange' };

export default function BirthRecordingPage() {
  const [data, setData] = useState<BirthRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);

  // Create modal
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();
  const [sexOptions, setSexOptions] = useState<{ id: string; value: string }[]>([]);
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [pendingGestations, setPendingGestations] = useState<GestationRecord[]>([]);
  const [pendingBreedingRecords, setPendingBreedingRecords] = useState<BreedingRecord[]>([]);
  const [selectedDamTag, setSelectedDamTag] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // Detail drawer
  const [detailOpen, setDetailOpen] = useState(false);
  const [selectedRecord, setSelectedRecord] = useState<BirthRecord | null>(null);

  const load = useCallback(async (p = page) => {
    setLoading(true);
    try {
      const res = await birthsApi.list({ page: p, pageSize: 10 });
      setData(res.data.items);
      setTotal(res.data.totalCount);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCreateModal = async () => {
    setModalOpen(true);
    form.resetFields();
    form.setFieldsValue({ offspring: [{}] });
    setSelectedDamTag('');
    try {
      const [sexRes, animalsRes, gestRes, breedRes] = await Promise.all([
        lookupsApi.sexOptions(),
        lookupsApi.animals(),
        gestationApi.list({ activeOnly: true, pageSize: 100 }),
        breedingRecordsApi.list({ result: 0, pageSize: 100 }),
      ]);
      setSexOptions(sexRes.data);
      setAnimals(animalsRes.data.items);
      setPendingGestations(gestRes.data.items);
      setPendingBreedingRecords(breedRes.data.items);
    } catch {
      // ignore
    }
  };

  const handleDamChange = (damId: string) => {
    const dam = animals.find(a => a.id === damId);
    setSelectedDamTag(dam?.tagNumber || '');
    // Try to find matching pending gestation for this dam
    const matchGestation = pendingGestations.find(g => g.animalId === damId);
    if (matchGestation) {
      form.setFieldValue('gestationRecordId', matchGestation.id);
    }
  };

  const handleAddOffspring = () => {
    const current = form.getFieldValue('offspring') || [];
    form.setFieldsValue({ offspring: [...current, {}] });
  };

  const handleRemoveOffspring = (index: number) => {
    const current = form.getFieldValue('offspring') || [];
    if (current.length <= 1) return;
    form.setFieldsValue({ offspring: current.filter((_: unknown, i: number) => i !== index) });
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      const payload: CreateBirthRecordPayload = {
        damId: values.damId,
        gestationRecordId: values.gestationRecordId || undefined,
        breedingRecordId: values.breedingRecordId || undefined,
        birthDate: values.birthDate.toISOString(),
        vetName: values.vetName,
        notes: values.notes,
        offspring: (values.offspring || []).map((off: Record<string, unknown>) => ({
          sexOptionId: off.sexOptionId,
          outcome: off.outcome ?? 0,
          birthWeightKg: off.birthWeightKg,
          name: off.name,
          notes: off.notes,
        })),
      };

      await birthsApi.create(payload);
      message.success('Birth record created successfully');
      setModalOpen(false);
      load(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await birthsApi.delete(id);
      message.success('Birth record deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const showDetail = (record: BirthRecord) => {
    setSelectedRecord(record);
    setDetailOpen(true);
  };

  const columns: ColumnsType<BirthRecord> = [
    {
      title: 'Dam',
      key: 'dam',
      render: (_, record) => (
        <span>
          <strong>{record.damTagNumber}</strong>
          {record.damName && <span style={{ color: '#999' }}> ({record.damName})</span>}
        </span>
      ),
    },
    {
      title: 'Birth Date',
      dataIndex: 'birthDate',
      key: 'birthDate',
      render: (text: string) => dayjs(text).format('YYYY-MM-DD'),
    },
    {
      title: 'Offspring',
      dataIndex: 'offspringCount',
      key: 'offspringCount',
      render: (val: number) => <Tag>{val}</Tag>,
    },
    {
      title: 'Alive',
      dataIndex: 'aliveCount',
      key: 'aliveCount',
      render: (val: number) => <Tag color="green">{val}</Tag>,
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
      width: 120,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} onClick={() => showDetail(record)} />
          <Popconfirm title="Delete this birth record?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const offspring = Form.useWatch('offspring', form) || [];

  return (
    <>
      <Card
        title="Birth Records"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
            Record Birth
          </Button>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: (p) => { setPage(p); load(p); } }}
        />
      </Card>

      {/* Create Birth Record Modal */}
      <Modal
        title="Record Birth"
        open={modalOpen}
        onOk={handleSubmit}
        onCancel={() => setModalOpen(false)}
        width={800}
        destroyOnClose
        confirmLoading={submitting}
        okText="Save"
      >
        <Form form={form} layout="vertical">
          <Form.Item name="damId" label="Dam" rules={[{ required: true, message: 'Select the dam' }]}>
            <Select
              showSearch
              placeholder="Select dam"
              optionFilterProp="label"
              onChange={handleDamChange}
              options={animals.map(a => ({ value: a.id, label: `${a.tagNumber} - ${a.name || 'Unnamed'}` }))}
            />
          </Form.Item>

          {selectedDamTag && (
            <div style={{ marginBottom: 16, padding: '8px 12px', background: '#f6f8fa', borderRadius: 6, border: '1px solid #d9d9d9' }}>
              <span style={{ color: '#666' }}>Offspring tags will be generated as: </span>
              <strong>{selectedDamTag}-1</strong>, <strong>{selectedDamTag}-2</strong>, ...
            </div>
          )}

          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="gestationRecordId" label="Gestation Record (optional)">
                <Select
                  allowClear
                  placeholder="Link to gestation record"
                  options={pendingGestations.map(g => ({
                    value: g.id,
                    label: `${dayjs(g.breedingDate).format('YYYY-MM-DD')} | ${g.animalTagNumber} (${g.daysUntilDue}d to due)`,
                  }))}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="breedingRecordId" label="Breeding Record (optional)">
                <Select
                  allowClear
                  placeholder="Link to breeding record"
                  options={pendingBreedingRecords.map(r => ({
                    value: r.id,
                    label: `${dayjs(r.breedingDate).format('YYYY-MM-DD')} | ${r.sireTagNumber} x ${r.damTagNumber}`,
                  }))}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item name="birthDate" label="Birth Date" rules={[{ required: true, message: 'Select birth date' }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>

          <Form.Item name="vetName" label="Veterinarian">
            <Input maxLength={200} placeholder="Vet name (optional)" />
          </Form.Item>

          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>

          {/* Offspring section */}
          <Card
            title={`Offspring (${offspring.length})`}
            size="small"
            style={{ marginBottom: 16 }}
            extra={
              <Button size="small" icon={<PlusOutlined />} onClick={handleAddOffspring}>
                Add Offspring
              </Button>
            }
          >
            {offspring.map((_: Record<string, unknown>, index: number) => (
              <Card
                key={index}
                size="small"
                style={{ marginBottom: 8 }}
                title={
                  <span>
                    Offspring #{index + 1}
                    {selectedDamTag && (
                      <Tag style={{ marginLeft: 8 }} color="blue">
                        Tag: {selectedDamTag}-{index + 1}
                      </Tag>
                    )}
                  </span>
                }
                extra={
                  offspring.length > 1 ? (
                    <Button size="small" danger icon={<DeleteOutlined />} onClick={() => handleRemoveOffspring(index)} />
                  ) : null
                }
              >
                <Row gutter={[16, 16]}>
                  <Col xs={24} sm={12} md={6}>
                    <Form.Item
                      name={[index, 'sexOptionId']}
                      label="Sex"
                      rules={[{ required: true, message: 'Required' }]}
                    >
                      <Select placeholder="Select sex" style={{ width: '100%' }}>
                        {sexOptions.map(s => (
                          <Select.Option key={s.id} value={s.id}>{s.value}</Select.Option>
                        ))}
                      </Select>
                    </Form.Item>
                  </Col>

                  <Col xs={24} sm={12} md={6}>
                    <Form.Item
                      name={[index, 'outcome']}
                      label="Outcome"
                      initialValue={0}
                    >
                      <Select style={{ width: '100%' }}>
                        <Select.Option value={0}>Alive</Select.Option>
                        <Select.Option value={1}>Stillborn</Select.Option>
                        <Select.Option value={2}>Weak</Select.Option>
                      </Select>
                    </Form.Item>
                  </Col>

                  <Col xs={24} sm={12} md={6}>
                    <Form.Item name={[index, 'birthWeightKg']} label="Weight (kg)">
                      <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>

                  <Col xs={24} sm={12} md={6}>
                    <Form.Item name={[index, 'name']} label="Name">
                      <Input maxLength={200} placeholder="Optional name" />
                    </Form.Item>
                  </Col>
                </Row>

                <Form.Item name={[index, 'notes']} label="Notes">
                  <Input maxLength={500} placeholder="Optional notes" />
                </Form.Item>
              </Card>
            ))}
          </Card>
        </Form>
      </Modal>

      {/* Detail Drawer */}
      <Modal
        title={selectedRecord ? `Birth Record - ${selectedRecord.damTagNumber}` : 'Birth Record'}
        open={detailOpen}
        onCancel={() => setDetailOpen(false)}
        footer={null}
        width={700}
      >
        {selectedRecord && (
          <>
            <Descriptions bordered size="small" column={2} style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Dam">{selectedRecord.damTagNumber} {selectedRecord.damName}</Descriptions.Item>
              <Descriptions.Item label="Birth Date">{dayjs(selectedRecord.birthDate).format('YYYY-MM-DD')}</Descriptions.Item>
              <Descriptions.Item label="Total Offspring">{selectedRecord.offspringCount}</Descriptions.Item>
              <Descriptions.Item label="Alive"><Tag color="green">{selectedRecord.aliveCount}</Tag></Descriptions.Item>
              <Descriptions.Item label="Vet">{selectedRecord.vetName || '-'}</Descriptions.Item>
              <Descriptions.Item label="Notes">{selectedRecord.notes || '-'}</Descriptions.Item>
            </Descriptions>

            <Card title="Offspring" size="small">
              {selectedRecord.offspring.length === 0 ? (
                <Empty description="No offspring recorded" />
              ) : (
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={selectedRecord.offspring}
                  columns={[
                    { title: 'Tag', dataIndex: 'tagNumber', key: 'tagNumber', render: (text: string) => <strong>{text}</strong> },
                    { title: 'Name', dataIndex: 'name', key: 'name', render: (text: string) => text || '-' },
                    { title: 'Sex', dataIndex: 'sexValue', key: 'sexValue', render: (text: string) => <Tag>{text}</Tag> },
                    {
                      title: 'Outcome',
                      dataIndex: 'outcome',
                      key: 'outcome',
                      render: (val: number) => <Tag color={OUTCOME_COLORS[val]}>{OUTCOME_LABELS[val]}</Tag>,
                    },
                    {
                      title: 'Weight',
                      dataIndex: 'birthWeightKg',
                      key: 'birthWeightKg',
                      render: (val?: number) => val ? `${val} kg` : '-',
                    },
                  ]}
                />
              )}
            </Card>
          </>
        )}
      </Modal>
    </>
  );
}
