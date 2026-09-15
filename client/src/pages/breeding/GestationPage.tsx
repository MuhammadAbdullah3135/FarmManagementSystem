import { useState, useEffect, useCallback } from 'react';
import { Card, Button, Tag, Space, Drawer, Form, DatePicker, Input, InputNumber, message, Descriptions, Empty, Tooltip, Modal } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import MobileTable from '../../components/MobileTable';
import { PlusOutlined, CloseCircleOutlined, HeartOutlined } from '@ant-design/icons';
import { gestationApi, breedingRecordsApi, type ConfirmPregnancyPayload, type LogHealthCheckPayload } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import dayjs from 'dayjs';
import type { GestationRecord, GestationHealthCheck, BreedingRecord } from '../../types';

const STAGE_LABELS: Record<number, string> = { 0: 'Early', 1: 'Mid', 2: 'Late', 3: 'Overdue' };
const STAGE_COLORS: Record<number, string> = { 0: 'blue', 1: 'green', 2: 'orange', 3: 'red' };

export default function GestationPage() {
  const [data, setData] = useState<GestationRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);

  // Health check drawer
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [selectedGestation, setSelectedGestation] = useState<GestationRecord | null>(null);
  const [healthChecks, setHealthChecks] = useState<GestationHealthCheck[]>([]);
  const [healthCheckLoading, setHealthCheckLoading] = useState(false);
  const [healthForm] = Form.useForm();

  // Confirm pregnancy modal
  const [confirmModalOpen, setConfirmModalOpen] = useState(false);
  const [pendingBreedingRecords, setPendingBreedingRecords] = useState<BreedingRecord[]>([]);
  const [confirmForm] = Form.useForm();

  const load = useCallback(async (p = page) => {
    setLoading(true);
    try {
      const res = await gestationApi.list({ activeOnly: true, page: p, pageSize: 10 });
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

  const openHealthDrawer = async (record: GestationRecord) => {
    setSelectedGestation(record);
    setDrawerOpen(true);
    setHealthCheckLoading(true);
    try {
      const res = await gestationApi.getHealthChecks(record.id);
      setHealthChecks(res.data);
    } catch {
      setHealthChecks([]);
    } finally {
      setHealthCheckLoading(false);
    }
  };

  const handleLogHealthCheck = async () => {
    if (!selectedGestation) return;
    try {
      const values = await healthForm.validateFields();
      const payload: LogHealthCheckPayload = {
        checkDate: values.checkDate.toISOString(),
        notes: values.notes,
        performedBy: values.performedBy,
        weightKg: values.weightKg,
      };
      await gestationApi.logHealthCheck(selectedGestation.id, payload);
      message.success('Health check logged');
      healthForm.resetFields();
      // Reload health checks
      const res = await gestationApi.getHealthChecks(selectedGestation.id);
      setHealthChecks(res.data);
      load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const openConfirmModal = async () => {
    setConfirmModalOpen(true);
    confirmForm.resetFields();
    try {
      const res = await breedingRecordsApi.list({ result: 0, pageSize: 100 });
      setPendingBreedingRecords(res.data.items);
    } catch {
      setPendingBreedingRecords([]);
    }
  };

  const handleConfirmPregnancy = async () => {
    try {
      const values = await confirmForm.validateFields();
      const payload: ConfirmPregnancyPayload = {
        breedingRecordId: values.breedingRecordId,
        confirmedDate: values.confirmedDate.toISOString(),
      };
      await gestationApi.confirm(payload);
      message.success('Pregnancy confirmed');
      setConfirmModalOpen(false);
      load(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleRevert = async (record: GestationRecord) => {
    try {
      await gestationApi.revert(record.id, 'Pregnancy reverted by user');
      message.success('Pregnancy reverted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const getDueColor = (daysUntilDue: number) => {
    if (daysUntilDue < 0) return '#ff4d4f';
    if (daysUntilDue <= 15) return '#ff7a45';
    if (daysUntilDue <= 30) return '#ffa940';
    return '#52c41a';
  };

  const columns: ColumnsType<GestationRecord> = [
    {
      title: 'Animal',
      key: 'animal',
      render: (_, record) => (
        <span>
          <strong>{record.animalTagNumber}</strong>
          {record.animalName && <span style={{ color: '#999' }}> ({record.animalName})</span>}
        </span>
      ),
    },
    {
      title: 'Sire',
      key: 'sire',
      render: (_, record) => record.sireTagNumber,
    },
    {
      title: 'Breeding Date',
      dataIndex: 'breedingDate',
      key: 'breedingDate',
      render: (text: string) => dayjs(text).format('YYYY-MM-DD'),
    },
    {
      title: 'Days Elapsed',
      dataIndex: 'daysElapsed',
      key: 'daysElapsed',
      render: (val: number) => `${val} days`,
    },
    {
      title: 'Expected Due',
      dataIndex: 'expectedDeliveryDate',
      key: 'expectedDelivery',
      render: (text: string, record) => (
        <span style={{ color: getDueColor(record.daysUntilDue), fontWeight: 'bold' }}>
          {dayjs(text).format('YYYY-MM-DD')}
        </span>
      ),
    },
    {
      title: 'Days Until Due',
      dataIndex: 'daysUntilDue',
      key: 'daysUntilDue',
      sorter: (a, b) => a.daysUntilDue - b.daysUntilDue,
      render: (val: number) => {
        const color = getDueColor(val);
        return (
          <Tooltip title={val < 0 ? `${Math.abs(val)} days overdue` : `${val} days remaining`}>
            <span style={{ color, fontWeight: 'bold', fontSize: 14 }}>
              {val < 0 ? `${Math.abs(val)}d overdue` : `${val}d`}
            </span>
          </Tooltip>
        );
      },
    },
    {
      title: 'Stage',
      dataIndex: 'currentStage',
      key: 'currentStage',
      render: (val: number) => <Tag color={STAGE_COLORS[val]}>{STAGE_LABELS[val]}</Tag>,
    },
    {
      title: 'Health Checks',
      dataIndex: 'healthCheckCount',
      key: 'healthCheckCount',
      render: (val: number) => val,
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 150,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<HeartOutlined />} onClick={() => openHealthDrawer(record)}>
            Health
          </Button>
          <Button size="small" danger icon={<CloseCircleOutlined />} onClick={() => handleRevert(record)}>
            Revert
          </Button>
        </Space>
      ),
    },
  ];


  return (
    <>
      <Card
        title="Active Pregnancies"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openConfirmModal}>
            Confirm Pregnancy
          </Button>
        }
      >
        {/* Summary stats */}
        <div style={{ display: 'flex', gap: 16, marginBottom: 16 }}>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#1677ff' }}>{total}</div>
              <div style={{ color: '#999' }}>Active Pregnancies</div>
            </div>
          </Card>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#ff4d4f' }}>
                {data.filter(d => d.daysUntilDue <= 15).length}
              </div>
              <div style={{ color: '#999' }}>Due Within 15 Days</div>
            </div>
          </Card>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#faad14' }}>
                {data.filter(d => d.currentStage === 3).length}
              </div>
              <div style={{ color: '#999' }}>Overdue</div>
            </div>
          </Card>
        </div>

        <MobileTable
          rowKey="id"
          fixedKeyColumn="animal"
          columns={columns}
          dataSource={data}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: (p) => { setPage(p); load(p); } }}
        />
      </Card>

      {/* Health Check Drawer */}
      <Drawer
        title={selectedGestation ? `Health Checks - ${selectedGestation.animalTagNumber}` : 'Health Checks'}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={500}
      >
        {selectedGestation && (
          <>
            <Descriptions bordered size="small" column={1} style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Animal">{selectedGestation.animalTagNumber} {selectedGestation.animalName}</Descriptions.Item>
              <Descriptions.Item label="Expected Delivery">{dayjs(selectedGestation.expectedDeliveryDate).format('YYYY-MM-DD')}</Descriptions.Item>
              <Descriptions.Item label="Days Until Due">
                <span style={{ color: getDueColor(selectedGestation.daysUntilDue), fontWeight: 'bold' }}>
                  {selectedGestation.daysUntilDue} days
                </span>
              </Descriptions.Item>
              <Descriptions.Item label="Stage">
                <Tag color={STAGE_COLORS[selectedGestation.currentStage]}>
                  {STAGE_LABELS[selectedGestation.currentStage]}
                </Tag>
              </Descriptions.Item>
            </Descriptions>

            <Card title="Log Health Check" size="small" style={{ marginBottom: 16 }}>
              <Form form={healthForm} layout="vertical" size="small">
                <Form.Item name="checkDate" label="Check Date" rules={[{ required: true }]}>
                  <DatePicker style={{ width: '100%' }} />
                </Form.Item>
                <Space style={{ display: 'flex' }}>
                  <Form.Item name="performedBy" label="Performed By" style={{ flex: 1 }}>
                    <Input maxLength={200} />
                  </Form.Item>
                  <Form.Item name="weightKg" label="Weight (kg)" style={{ flex: 1 }}>
                    <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                  </Form.Item>
                </Space>
                <Form.Item name="notes" label="Notes">
                  <Input.TextArea rows={2} maxLength={2000} />
                </Form.Item>
                <Button type="primary" icon={<PlusOutlined />} onClick={handleLogHealthCheck}>
                  Log Check
                </Button>
              </Form>
            </Card>

            <Card title="Health Check History" size="small">
              {healthCheckLoading ? (
                <Empty description="Loading..." />
              ) : healthChecks.length === 0 ? (
                <Empty description="No health checks recorded" />
              ) : (
                <div>
                  {healthChecks.map(check => (
                    <Card key={check.id} size="small" style={{ marginBottom: 8 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                        <strong>{dayjs(check.checkDate).format('YYYY-MM-DD')}</strong>
                        {check.weightKg && <Tag>{check.weightKg} kg</Tag>}
                      </div>
                      {check.performedBy && <div style={{ color: '#999', marginBottom: 4 }}>By: {check.performedBy}</div>}
                      {check.notes && <div>{check.notes}</div>}
                    </Card>
                  ))}
                </div>
              )}
            </Card>
          </>
        )}
      </Drawer>

      {/* Confirm Pregnancy Modal */}
      <Modal
        title="Confirm Pregnancy"
        open={confirmModalOpen}
        onOk={handleConfirmPregnancy}
        onCancel={() => setConfirmModalOpen(false)}
        width={500}
        destroyOnClose
      >
        <Form form={confirmForm} layout="vertical">
          <Form.Item name="breedingRecordId" label="Breeding Record (Pending)" rules={[{ required: true }]}>
            <select style={{ width: '100%', padding: 8 }}>
              <option value="">Select a pending breeding record</option>
              {pendingBreedingRecords.map(r => (
                <option key={r.id} value={r.id}>
                  {dayjs(r.breedingDate).format('YYYY-MM-DD')} | {r.sireTagNumber} x {r.damTagNumber}
                </option>
              ))}
            </select>
          </Form.Item>
          <Form.Item name="confirmedDate" label="Confirmation Date" rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
