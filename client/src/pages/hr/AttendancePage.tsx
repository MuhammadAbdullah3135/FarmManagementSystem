import React, { useState } from 'react';
import { formatDate } from '../../i18n/format';
import {
  Alert, Button, Card, Col, DatePicker, Form, Input, Modal, Row, Select, Space, Table, Tag, Typography, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { DirectionalIcon } from '../../i18n/DirectionalIcon';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { attendanceApi } from '../../api/attendance';
import { employeesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import { useCachedQuery } from '../../offline/cachedQuery';
import SyncAgeLabel from '../../components/SyncAgeLabel';
import OutboxTable from '../../components/OutboxTable';
import {
  ATTENDANCE_CHECK_IN,
  ATTENDANCE_CHECK_OUT,
  type AttendanceMutationPayload,
} from '../../offline/mutationKinds';
import { enqueueMutation, type OutboxItem } from '../../offline/outbox';
import { requestFlush } from '../../offline/syncEngine';
import { useSyncStore } from '../../offline/syncStatus';
import { useOfflineStore } from '../../offline/connectivity';
import { useOutboxItems } from '../../offline/useOutbox';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import type { AttendanceRecord, AttendanceStatus, Employee } from '../../types';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const STATUS_COLORS: Record<AttendanceStatus, string> = {
  Present: 'green',
  Absent: 'red',
  Late: 'orange',
  HalfDay: 'blue',
  Leave: 'purple',
  Holiday: 'cyan',
};

const ATTENDANCE_STATUSES: AttendanceStatus[] = ['Present', 'Absent', 'Late', 'HalfDay', 'Leave', 'Holiday'];

/**
 * Attendance, with or without a connection.
 *
 * Check-in and check-out are queued exactly as weights are (4.5.4): one write path, the device's
 * own time, and the server's answer replacing the row once it has it. The register and the
 * employee list are cached work lists, so the page is usable with no signal — including the
 * state of the day you are looking at, because a page that can queue a check-in but cannot show
 * who is already in would make the user guess.
 *
 * Both buttons stay enabled offline on purpose. The cached register may be stale, and a device
 * that refused a legitimate check-out because its cache had not caught up would be worse than
 * one that sends it and shows the server's message if it turns out to be wrong. The queue's
 * oldest-first order is what makes the common case work: a check-in queued before a check-out
 * reaches the server first, so the day exists by the time the check-out is applied.
 *
 * The manual entry dialog stays a live request: it is an administrative correction, not
 * fieldwork, and 4.5.5's offline targets are the check-in and the check-out.
 */
const AttendancePage: React.FC = () => {const { t } = useTranslation('hr'); 
  const [page, setPage] = useState(1);
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null]>([dayjs(), dayjs()]);
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();

  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farmId = useFarmStore((state) => state.activeFarm?.id ?? null);
  const scope = accountId && farmId ? { accountId, farmId } : null;

  const isOnline = useOfflineStore((store) => store.isOnline);
  const isFlushing = useSyncStore((store) => store.isFlushing);

  /** Today's view, first page — the cached work list. Anything else is a live query. */
  const isDefaultRange = Boolean(dateRange[0]?.isSame(dayjs(), 'day') && dateRange[1]?.isSame(dayjs(), 'day'));

  const employeesQuery = useCachedQuery<Employee>({
    collection: 'employees',
    variant: 'options',
    // The whole roster, so a delta is exactly right here: nothing is dropped by paging.
    supportsDelta: true,
    fetcher: async (cursor) => {
      const res = await employeesApi.list({ page: 1, pageSize: 100, updatedSince: cursor });
      const data = res.data;
      return {
        rows: data.items,
        total: data.totalCount,
        // Reported on a full read as well, so the next read can be a delta.
        cursor: data.cursor,
        delta: cursor ? {
          deletedIds: data.deletedIds ?? [],
          requiresFullSync: data.requiresFullSync,
        } : undefined,
      };
    },
    onError: (err) => message.error(getApiError(err)),
  });

  const recordsQuery = useCachedQuery<AttendanceRecord>({
    collection: 'attendance',
    variant: 'firstPage',
    cacheable: page === 1 && isDefaultRange,
    paramsKey: `${page}|${dateRange[0]?.format('YYYY-MM-DD') ?? ''}|${dateRange[1]?.format('YYYY-MM-DD') ?? ''}`,
    fetcher: async () => {
      const res = await attendanceApi.list({
        from: dateRange[0]?.toISOString(),
        to: dateRange[1]?.toISOString(),
        page,
        pageSize: 10,
      });
      return { rows: res.data.items, total: res.data.totalCount };
    },
    onError: (err) => message.error(getApiError(err)),
  });

  // Only this page's workflows: the weight page's queue and this one's must not show each
  // other's records.
  const items = useOutboxItems(scope, { kinds: [ATTENDANCE_CHECK_IN, ATTENDANCE_CHECK_OUT] });
  const quarantined = items.filter((item) => item.status === 'quarantined');

  const pendingFor = (kind: string) => {
    const map = new Map<string, OutboxItem>();
    for (const item of items) {
      if (item.status === 'pending' && item.kind === kind) map.set(item.targetId, item);
    }
    return map;
  };
  const pendingCheckIns = pendingFor(ATTENDANCE_CHECK_IN);
  const pendingCheckOuts = pendingFor(ATTENDANCE_CHECK_OUT);

  const employees = employeesQuery.rows;
  const records = recordsQuery.rows;

  const employeeLabel = (item: OutboxItem) => {
    const employee = employees.find((e) => e.id === item.targetId);
    return employee ? `${employee.firstName} ${employee.lastName}` : null;
  };

  const queueAttendance = async (employee: Employee, kind: string, verb: string) => {
    if (!scope) {
      message.error(t('selectAFarmFirst'));
      return;
    }

    // The device's clock, captured now: the time the employee was at the gate, not the time the
    // request reaches the server. 5.3's endpoint validates it against the same tolerance the
    // live endpoint applies.
    const occurredAt = new Date().toISOString();
    const payload: AttendanceMutationPayload = { employeeId: employee.id, occurredAt };

    const queued = await enqueueMutation<AttendanceMutationPayload>({
      scope,
      kind,
      targetId: employee.id,
      occurredAt,
      payload,
    });

    if (!queued.item) {
      // An attendance record that is not queued is one the server will never see, so this must
      // fail loudly rather than look like a save — with the reason, which since 5.6 may be the
      // device being full or the session being too old to deliver it.
      message.error(queued.message ?? 'Nothing was recorded.');
      return;
    }

    if (queued.warning) message.warning(queued.warning);

    message.success(
      isOnline
        ? `${verb} ${employee.firstName}. Sending now.`
        : `${verb} ${employee.firstName} on this device. It will sync when you are online.`,
    );

    void requestFlush();
  };

  const handleUpsert = async () => {
    try {
      const values = await form.validateFields();
      await attendanceApi.upsert({
        employeeId: values.employeeId,
        date: values.date.format('YYYY-MM-DD'),
        status: values.status,
        checkInAt: values.checkInAt?.toISOString(),
        checkOutAt: values.checkOutAt?.toISOString(),
        notes: values.notes,
      });
      message.success(t('attendanceRecorded'));
      setModalOpen(false);
      recordsQuery.refresh();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await attendanceApi.remove(id);
      message.success(t('recordDeleted'));
      recordsQuery.refresh();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<AttendanceRecord> = [
    { title: t('employee'), dataIndex: 'employeeName' },
    { title: t('date'), dataIndex: 'date', render: (d: string) => formatDate(d) },
    {
      title: t('status'),
      dataIndex: 'status',
      render: (s: AttendanceStatus) => <Tag color={STATUS_COLORS[s]}>{s}</Tag>,
    },
    {
      title: t('checkIn'),
      dataIndex: 'checkInAt',
      render: (d?: string) => d ? dayjs(d).format('HH:mm') : '-',
    },
    {
      title: t('checkOut'),
      dataIndex: 'checkOutAt',
      render: (d?: string) => d ? dayjs(d).format('HH:mm') : '-',
    },
    {
      title: t('hours'),
      dataIndex: 'hoursWorked',
      render: (h?: number) => h != null ? `${h}h` : '-',
    },
    { title: t('notes'), dataIndex: 'notes', ellipsis: true, render: (n?: string) => n ?? '-' },
    {
      title: t('actions'),
      render: (_, r) => (
        <Space>
          <Button size="small" danger onClick={() => handleDelete(r.id)}>{t('delete')}</Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        {!isOnline && (
          <Alert
            type="warning"
            showIcon
            message={t('youAreOffline')}
            description={t('checkInAndCheckOutStillWorkThey')}
          />
        )}

        {quarantined.length > 0 && (
          <Alert
            type="error"
            showIcon
            message={`${quarantined.length} attendance record${quarantined.length === 1 ? '' : 's'} could not be saved`}
            description={t('theServerRefusedThemEachMessageBelowSays')}
          />
        )}

        <Card title={t('todaySAttendance')}>
          <Space wrap>
            {employees.filter((e) => e.isActive).map((emp) => {
              const pendingIn = pendingCheckIns.get(emp.id);
              const pendingOut = pendingCheckOuts.get(emp.id);
              return (
                <Card key={emp.id} size="small" style={{ width: 220 }}>
                  <div style={{ marginBottom: 8 }}>{emp.firstName} {emp.lastName}</div>
                  {pendingIn && (
                    <div style={{ marginBottom: 8 }}>
                      <Tag color="blue">{t('checkInWaitingToSync')}</Tag>
                    </div>
                  )}
                  {pendingOut && (
                    <div style={{ marginBottom: 8 }}>
                      <Tag color="blue">{t('checkOutWaitingToSync')}</Tag>
                    </div>
                  )}
                  <Space>
                    <Button
                      size="small"
                      type="primary"
                      icon={<DirectionalIcon role="enter" />}
                      onClick={() => void queueAttendance(emp, ATTENDANCE_CHECK_IN, 'Checked in')}
                    >
                      {t('in')}
                    </Button>
                    <Button
                      size="small"
                      icon={<DirectionalIcon role="leave" />}
                      onClick={() => void queueAttendance(emp, ATTENDANCE_CHECK_OUT, 'Checked out')}
                    >
                      {t('out')}
                    </Button>
                  </Space>
                </Card>
              );
            })}
            {employees.length === 0 && (
              <Text type="secondary">
                {t('noEmployeesAreStoredOnThisDeviceYet')}
              </Text>
            )}
          </Space>

          {employeesQuery.lastSyncedAt && (
            <div style={{ marginTop: 12 }}>
              <SyncAgeLabel lastSyncedAt={employeesQuery.lastSyncedAt} />
            </div>
          )}
        </Card>

        <Card
          title={t('attendanceRecords')}
          extra={
            <Space>
              <DatePicker.RangePicker value={dateRange} onChange={(v) => v && setDateRange(v)} />
              <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); form.setFieldsValue({ date: dayjs(), status: 'Present' }); setModalOpen(true); }}>
                {t('manualEntry')}
              </Button>
            </Space>
          }
        >
          {/* The freshness label describes these rows: a cached register must never look live. */}
          {recordsQuery.lastSyncedAt && (
            <div style={{ marginBottom: 8 }}>
              <SyncAgeLabel lastSyncedAt={recordsQuery.lastSyncedAt} />
            </div>
          )}
          <Table
            rowKey="id"
            columns={columns}
            dataSource={records}
            loading={recordsQuery.isLoading}
            pagination={{ current: page, total: recordsQuery.total, pageSize: 10, onChange: setPage }}
          />
        </Card>

        <Card
          title={t('recordedOnThisDevice')}
          extra={<Text type="secondary">{t('keptForADayAfterTheySync')}</Text>}
        >
          <OutboxTable
            items={items}
            targetLabel={employeeLabel}
            loading={isFlushing}
            emptyText={t('checkInsAndCheckOutsYouRecordShow')}
          />
          <div style={{ marginTop: 12 }}>
            <Button disabled={items.length === 0} loading={isFlushing} onClick={() => void requestFlush({ force: true })}>
              {t('syncNow')}
            </Button>
          </div>
        </Card>
      </Space>

      <Modal title={t('manualAttendance')} open={modalOpen} onOk={handleUpsert} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="employeeId" label={t('employee')} rules={[{ required: true }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
              style={{ width: '100%' }}
              popupMatchSelectWidth={false}
            />
          </Form.Item>
          <Form.Item name="date" label={t('date')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="status" label={t('status')} rules={[{ required: true }]}>
            <Select options={ATTENDANCE_STATUSES.map((s) => ({ value: s, label: s }))} style={{ width: '100%' }} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="checkInAt" label={t('checkIn')}>
                <DatePicker showTime style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="checkOutAt" label={t('checkOut')}>
                <DatePicker showTime style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="notes" label={t('notes')}>
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default AttendancePage;
