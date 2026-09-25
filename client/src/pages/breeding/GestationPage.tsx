import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Tag, Space, Col, Drawer, Form, DatePicker, Input, InputNumber, message, Descriptions, Empty, Tooltip, Modal, Row } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, CloseCircleOutlined, HeartOutlined } from '@ant-design/icons';
import { gestationApi, breedingRecordsApi, type ConfirmPregnancyPayload, type LogHealthCheckPayload } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import { formatDate } from '../../i18n/format';

import type { GestationRecord, GestationHealthCheck, BreedingRecord } from '../../types';
import { useTranslation } from 'react-i18next';

const STAGE_LABELS: Record<number, string> = { 0: 'Early', 1: 'Mid', 2: 'Late', 3: 'Overdue' };
const STAGE_COLORS: Record<number, string> = { 0: 'blue', 1: 'green', 2: 'orange', 3: 'red' };

export default function GestationPage() {const { t } = useTranslation('breeding'); 
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
      message.success(t('healthCheckLogged'));
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
      message.success(t('pregnancyConfirmed'));
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
      message.success(t('pregnancyReverted'));
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
      title: t('animal'),
      key: 'animal',
      render: (_, record) => (
        <span>
          <strong>{record.animalTagNumber}</strong>
          {record.animalName && <span style={{ color: '#999' }}> ({record.animalName})</span>}
        </span>
      ),
    },
    {
      title: t('sire'),
      key: 'sire',
      render: (_, record) => record.sireTagNumber,
    },
    {
      title: t('breedingDate'),
      dataIndex: 'breedingDate',
      key: 'breedingDate',
      render: (text: string) => formatDate(text),
    },
    {
      title: t('daysElapsed'),
      dataIndex: 'daysElapsed',
      key: 'daysElapsed',
      render: (val: number) => `${val} days`,
    },
    {
      title: t('expectedDue'),
      dataIndex: 'expectedDeliveryDate',
      key: 'expectedDelivery',
      render: (text: string, record) => (
        <span style={{ color: getDueColor(record.daysUntilDue), fontWeight: 'bold' }}>
          {formatDate(text)}
        </span>
      ),
    },
    {
      title: t('daysUntilDue'),
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
      title: t('stage'),
      dataIndex: 'currentStage',
      key: 'currentStage',
      render: (val: number) => <Tag color={STAGE_COLORS[val]}>{STAGE_LABELS[val]}</Tag>,
    },
    {
      title: t('healthChecks'),
      dataIndex: 'healthCheckCount',
      key: 'healthCheckCount',
      render: (val: number) => val,
    },
    {
      title: t('actions'),
      key: 'actions',
      width: 150,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<HeartOutlined />} onClick={() => openHealthDrawer(record)}>
            {t('health')}
          </Button>
          <Button size="small" danger icon={<CloseCircleOutlined />} onClick={() => handleRevert(record)}>
            {t('revert')}
          </Button>
        </Space>
      ),
    },
  ];


  return (
    <>
      <Card
        title={t('activePregnancies')}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openConfirmModal}>
            {t('confirmPregnancy')}
          </Button>
        }
      >
        {/* Summary stats */}
        <div style={{ display: 'flex', gap: 16, marginBottom: 16 }}>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#1677ff' }}>{total}</div>
              <div style={{ color: '#999' }}>{t('activePregnancies')}</div>
            </div>
          </Card>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#ff4d4f' }}>
                {data.filter(d => d.daysUntilDue <= 15).length}
              </div>
              <div style={{ color: '#999' }}>{t('dueWithin15Days')}</div>
            </div>
          </Card>
          <Card size="small" style={{ flex: 1 }}>
            <div style={{ textAlign: 'center' }}>
              <div style={{ fontSize: 24, fontWeight: 'bold', color: '#faad14' }}>
                {data.filter(d => d.currentStage === 3).length}
              </div>
              <div style={{ color: '#999' }}>{t('overdue')}</div>
            </div>
          </Card>
        </div>

        <Table
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: (p) => { setPage(p); load(p); } }}
        />
      </Card>

      {/* Health Check Drawer */}
      <Drawer
        title={selectedGestation ? `Health Checks - ${selectedGestation.animalTagNumber}` : t('healthChecks')}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={500}
      >
        {selectedGestation && (
          <>
            <Descriptions bordered size="small" column={1} style={{ marginBottom: 16 }}>
              <Descriptions.Item label={t('animal')}>{selectedGestation.animalTagNumber} {selectedGestation.animalName}</Descriptions.Item>
              <Descriptions.Item label={t('expectedDelivery')}>{formatDate(selectedGestation.expectedDeliveryDate)}</Descriptions.Item>
              <Descriptions.Item label={t('daysUntilDue')}>
                <span style={{ color: getDueColor(selectedGestation.daysUntilDue), fontWeight: 'bold' }}>
                  {selectedGestation.daysUntilDue} {t('days')}
                </span>
              </Descriptions.Item>
              <Descriptions.Item label={t('stage')}>
                <Tag color={STAGE_COLORS[selectedGestation.currentStage]}>
                  {STAGE_LABELS[selectedGestation.currentStage]}
                </Tag>
              </Descriptions.Item>
            </Descriptions>

            <Card title={t('logHealthCheck')} size="small" style={{ marginBottom: 16 }}>
              <Form form={healthForm} layout="vertical" size="small">
                <Form.Item name="checkDate" label={t('checkDate')} rules={[{ required: true }]}>
                  <DatePicker style={{ width: '100%' }} />
                </Form.Item>
                <Row gutter={[16, 16]}>
                  <Col xs={24} sm={12}>
                    <Form.Item name="performedBy" label={t('performedBy')}>
                      <Input maxLength={200} />
                    </Form.Item>
                  </Col>
                  <Col xs={24} sm={12}>
                    <Form.Item name="weightKg" label={t('weightKg')}>
                      <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                </Row>
                <Form.Item name="notes" label={t('notes')}>
                  <Input.TextArea rows={2} maxLength={2000} />
                </Form.Item>
                <Button type="primary" icon={<PlusOutlined />} onClick={handleLogHealthCheck}>
                  {t('logCheck')}
                </Button>
              </Form>
            </Card>

            <Card title={t('healthCheckHistory')} size="small">
              {healthCheckLoading ? (
                <Empty description={t('loading')} />
              ) : healthChecks.length === 0 ? (
                <Empty description={t('noHealthChecksRecorded')} />
              ) : (
                <div>
                  {healthChecks.map(check => (
                    <Card key={check.id} size="small" style={{ marginBottom: 8 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                        <strong>{formatDate(check.checkDate)}</strong>
                        {check.weightKg && <Tag>{check.weightKg} {t('kg')}</Tag>}
                      </div>
                      {check.performedBy && <div style={{ color: '#999', marginBottom: 4 }}>{t('by')} {check.performedBy}</div>}
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
        title={t('confirmPregnancy')}
        open={confirmModalOpen}
        onOk={handleConfirmPregnancy}
        onCancel={() => setConfirmModalOpen(false)}
        width={500}
        destroyOnClose
      >
        <Form form={confirmForm} layout="vertical">
          <Form.Item name="breedingRecordId" label={t('breedingRecordPending')} rules={[{ required: true }]}>
            <select style={{ width: '100%', padding: 8 }}>
              <option value="">{t('selectAPendingBreedingRecord')}</option>
              {pendingBreedingRecords.map(r => (
                <option key={r.id} value={r.id}>
                  {formatDate(r.breedingDate)} | {r.sireTagNumber} {t('x')} {r.damTagNumber}
                </option>
              ))}
            </select>
          </Form.Item>
          <Form.Item name="confirmedDate" label={t('confirmationDate')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
