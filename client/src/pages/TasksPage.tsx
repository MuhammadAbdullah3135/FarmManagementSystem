import React, { useState } from 'react';
import {
  Alert, Button, Card, Checkbox, Col, DatePicker, Form, Input, Modal, Popconfirm, Row, Select, Space, Table, Tag,
  Typography, message,
} from 'antd';
import { PlusOutlined, PlayCircleOutlined, CheckOutlined, StopOutlined, UndoOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { formatDate } from '../i18n/format';
import dayjs from 'dayjs';
import { tasksApi } from '../api/tasks';
import { employeesApi } from '../api/hr';
import { getApiError } from '../api/farmApi';
import { useCachedQuery } from '../offline/cachedQuery';
import SyncAgeLabel from '../components/SyncAgeLabel';
import OutboxTable from '../components/OutboxTable';
import { TASK_COMPLETE, type TaskCompletionPayload } from '../offline/mutationKinds';
import { enqueueMutation, type OutboxItem } from '../offline/outbox';
import { requestFlush } from '../offline/syncEngine';
import { useSyncStore } from '../offline/syncStatus';
import { useOfflineStore } from '../offline/connectivity';
import { useOutboxItems } from '../offline/useOutbox';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Employee, FarmTask, FarmTaskPriority, FarmTaskStatus } from '../types';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const PRIORITY_COLORS: Record<FarmTaskPriority, string> = { Low: 'default', Medium: 'blue', High: 'red' };
const STATUS_COLORS: Record<FarmTaskStatus, string> = { Pending: 'gold', InProgress: 'processing', Completed: 'green', Cancelled: 'default' };

const TasksPage: React.FC = () => {const { t } = useTranslation('tasks'); 
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState<{
    status?: string;
    priority?: string;
    assignee?: string;
    overdueOnly?: boolean;
  }>({});
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<FarmTask | null>(null);
  const [notesTarget, setNotesTarget] = useState<{ id: string; action: 'complete' | 'cancel' } | null>(null);
  const [notesValue, setNotesValue] = useState('');
  const [form] = Form.useForm();

  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farmId = useFarmStore((state) => state.activeFarm?.id ?? null);
  const scope = accountId && farmId ? { accountId, farmId } : null;

  const isOnline = useOfflineStore((store) => store.isOnline);
  const isFlushing = useSyncStore((store) => store.isFlushing);

  /**
   * The first page with no filters is the cached work list (Phase 5.2) — the view a
   * field worker opens with no signal. Filtered and paginated views are live queries:
   * they are deliberately never written to the device.
   */
  const hasFilters = Boolean(filters.status || filters.priority || filters.assignee || filters.overdueOnly);
  const tasksQuery = useCachedQuery<FarmTask>({
    collection: 'tasks',
    variant: 'firstPage',
    // Delta-capable, but only the cached view uses it: a filtered or later page is a live query
    // whose rows were never stored, so there is nothing for a delta to merge into.
    supportsDelta: true,
    cacheable: page === 1 && !hasFilters,
    paramsKey: `${page}|${filters.status ?? ''}|${filters.priority ?? ''}|${filters.assignee ?? ''}|${filters.overdueOnly ? '1' : '0'}`,
    fetcher: async (cursor) => {
      const res = await tasksApi.list({ ...filters, page, pageSize: 10, updatedSince: cursor });
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

  // The assignee options come from the same cached employees collection; without them
  // the offline list still renders, but its filter cannot name anyone.
  const employeesQuery = useCachedQuery<Employee>({
    collection: 'employees',
    variant: 'options',
    supportsDelta: true,
    fetcher: async (cursor) => {
      const res = await employeesApi.list({ page: 1, pageSize: 100, updatedSince: cursor });
      const data = res.data;
      return {
        rows: data.items,
        total: data.totalCount,
        cursor: data.cursor,
        delta: cursor ? {
          deletedIds: data.deletedIds ?? [],
          requiresFullSync: data.requiresFullSync,
        } : undefined,
      };
    },
    onError: (err) => message.error(getApiError(err)),
  });

  const tasks = tasksQuery.rows;
  const employees = employeesQuery.rows;

  // Completions this device still owes the server, keyed by task: a row that has one already
  // waiting says so instead of offering to complete it a second time.
  const queueItems = useOutboxItems(scope, { kinds: [TASK_COMPLETE] });
  const pendingCompletions = new Map<string, OutboxItem>();
  for (const item of queueItems) {
    if (item.status === 'pending') pendingCompletions.set(item.targetId, item);
  }
  const quarantined = queueItems.filter((item) => item.status === 'quarantined');

  const taskLabel = (item: OutboxItem) => tasks.find((task) => task.id === item.targetId)?.title ?? null;

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ priority: 'Medium', dueDate: dayjs().add(7, 'day') });
    setModalOpen(true);
  };

  const openEdit = (task: FarmTask) => {
    setEditing(task);
    form.setFieldsValue({
      title: task.title,
      description: task.description,
      priority: task.priority,
      dueDate: dayjs(task.dueDate),
      assignedEmployeeId: task.assignedEmployeeId,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        title: values.title,
        description: values.description,
        priority: values.priority,
        dueDate: values.dueDate.format('YYYY-MM-DD'),
        assignedEmployeeId: values.assignedEmployeeId,
      };
      if (editing) {
        await tasksApi.update(editing.id, data);
        message.success(t('taskUpdated'));
      } else {
        await tasksApi.create(data);
        message.success(t('taskCreated'));
      }
      setModalOpen(false);
      tasksQuery.refresh();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await tasksApi.remove(id);
      message.success(t('taskDeleted'));
      tasksQuery.refresh();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  /**
   * Completion is fieldwork, so it goes through the queue exactly as a weight does: one write
   * path, the device's own time, and the server's answer replacing the row once it has it.
   * Offline the completion waits on the device; online it lands within a second either way.
   * A completion the server refuses (the task was cancelled, or someone else completed it with
   * different notes) is quarantined below with the server's own message rather than lost.
   */
  const queueCompletion = async (taskId: string, notes: string) => {
    if (!scope) {
      message.error(t('selectAFarmFirst'));
      return;
    }

    const occurredAt = new Date().toISOString();
    const payload: TaskCompletionPayload = {
      taskId,
      completionNotes: notes.trim() ? notes.trim() : null,
      occurredAt,
    };

    const queued = await enqueueMutation<TaskCompletionPayload>({
      scope,
      kind: TASK_COMPLETE,
      targetId: taskId,
      occurredAt,
      payload,
    });

    if (!queued.item) {
      // With the reason: since 5.6 a refusal can be the device being full or the session being
      // too old to deliver it, and "storage is broken" would be the wrong one to show.
      message.error(queued.message ?? 'The completion was not saved.');
      return;
    }

    if (queued.warning) message.warning(queued.warning);

    message.success(
      isOnline
        ? 'Task completed. Sending now.'
        : 'Task completed on this device. It will sync when you are online.',
    );

    setNotesTarget(null);
    setNotesValue('');
    void requestFlush();
  };

  const handleAction = async () => {
    if (!notesTarget) return;

    if (notesTarget.action === 'complete') {
      await queueCompletion(notesTarget.id, notesValue);
      return;
    }

    // Cancelling is a desk action and stays a live request: it is not one of the offline
    // workflows, and pretending otherwise would queue a transition the server owns.
    try {
      await tasksApi.cancel(notesTarget.id, notesValue || undefined);
      message.success(t('taskCancelled'));
      setNotesTarget(null);
      setNotesValue('');
      tasksQuery.refresh();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FarmTask> = [
    { title: t('title'), dataIndex: 'title', ellipsis: true },
    {
      title: t('priority'),
      dataIndex: 'priority',
      width: 100,
      render: (p: FarmTaskPriority) => <Tag color={PRIORITY_COLORS[p]}>{p}</Tag>,
    },
    {
      title: t('status'),
      dataIndex: 'status',
      width: 130,
      render: (s: FarmTaskStatus, r) => (
        <Space direction="vertical" size={2}>
          <Tag color={STATUS_COLORS[s]}>{s}</Tag>
          {pendingCompletions.has(r.id) && <Tag color="blue">{t('waitingToSync')}</Tag>}
        </Space>
      ),
    },
    {
      title: t('due'),
      dataIndex: 'dueDate',
      render: (d: string, r) => (
        <span style={r.isOverdue ? { color: '#ff4d4f', fontWeight: 600 } : undefined}>
          {formatDate(d)}
          {r.isOverdue && ' (overdue)'}
        </span>
      ),
    },
    { title: t('assignee'), dataIndex: 'assignedEmployeeName', render: (n?: string) => n ?? '-' },
    {
      title: t('actions'),
      render: (_, r) => (
        <Space size={4}>
          {r.status === 'Pending' && (
            <Button size="small" icon={<PlayCircleOutlined />} aria-label={t('startTask')}
              onClick={async () => { await tasksApi.start(r.id); tasksQuery.refresh(); }} />
          )}
          {(r.status === 'Pending' || r.status === 'InProgress') && (
            <>
              {/* A completion already queued for this task: it is the server's row now, so
                  there is nothing to send a second time. */}
              {!pendingCompletions.has(r.id) && (
                <Button size="small" type="primary" icon={<CheckOutlined />} aria-label={t('completeTask')}
                  onClick={() => setNotesTarget({ id: r.id, action: 'complete' })} />
              )}
              <Button size="small" danger icon={<StopOutlined />} aria-label={t('cancelTask')}
                onClick={() => setNotesTarget({ id: r.id, action: 'cancel' })} />
            </>
          )}
          {(r.status === 'Completed' || r.status === 'Cancelled') && (
            <Button size="small" icon={<UndoOutlined />} aria-label={t('reopenTask')}
              onClick={async () => { await tasksApi.reopen(r.id); tasksQuery.refresh(); }} />
          )}
          <Button size="small" onClick={() => openEdit(r)}>{t('edit')}</Button>
          <Popconfirm title={t('delete')} onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>{t('delete2')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title={t('farmTasks')}
      extra={
        <Space wrap>
          <Select allowClear placeholder={t('status')} style={{ width: 120 }} value={filters.status}
            onChange={(v) => setFilters((f) => ({ ...f, status: v }))}
            options={['Pending', 'InProgress', 'Completed', 'Cancelled'].map((s) => ({ value: s, label: s }))} />
          <Select allowClear placeholder={t('priority')} style={{ width: 120 }} value={filters.priority}
            onChange={(v) => setFilters((f) => ({ ...f, priority: v }))}
            options={['Low', 'Medium', 'High'].map((p) => ({ value: p, label: p }))} />
          <Select allowClear placeholder={t('assignee')} style={{ width: 180 }} value={filters.assignee}
            onChange={(v) => setFilters((f) => ({ ...f, assignee: v }))}
            options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          <Checkbox checked={filters.overdueOnly}
            onChange={(e) => setFilters((f) => ({ ...f, overdueOnly: e.target.checked }))}>
            {t('overdueOnly')}
          </Checkbox>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('newTask')}</Button>
        </Space>
      }
    >
      {/* The freshness label describes these rows: cached views must never look live. */}
      {tasksQuery.lastSyncedAt && (
        <div style={{ marginBottom: 8 }}>
          <SyncAgeLabel lastSyncedAt={tasksQuery.lastSyncedAt} />
        </div>
      )}

      {!isOnline && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('youAreOffline')}
          description={t('completingATaskStillWorksItIsStored')}
        />
      )}

      {quarantined.length > 0 && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          message={`${quarantined.length} completion${quarantined.length === 1 ? '' : 's'} could not be saved`}
          description={t('theServerRefusedThemForExampleATask')}
        />
      )}

      <Table
        rowKey="id"
        columns={columns}
        dataSource={tasks}
        loading={tasksQuery.isLoading}
        pagination={{ current: page, total: tasksQuery.total, pageSize: 10, onChange: setPage }}
      />

      <div style={{ marginTop: 16 }}>
        <Space style={{ marginBottom: 8 }}>
          <Text strong>{t('recordedOnThisDevice')}</Text>
          <Text type="secondary">{t('keptForADayAfterTheySync')}</Text>
          <Button
            size="small"
            disabled={queueItems.length === 0}
            loading={isFlushing}
            onClick={() => void requestFlush({ force: true })}
          >
            {t('syncNow')}
          </Button>
        </Space>
        <OutboxTable
          items={queueItems}
          targetLabel={taskLabel}
          emptyText={t('completionsYouRecordShowUpHereUntilThe')}
        />
      </div>

      <Modal title={editing ? t('editTask') : t('newTask')} open={modalOpen} onOk={handleSave} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="title" label={t('title')} rules={[{ required: true }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="priority" label={t('priority')} rules={[{ required: true }]}>
                <Select options={['Low', 'Medium', 'High'].map((p) => ({ value: p, label: p }))} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="dueDate" label={t('dueDate')} rules={[{ required: true }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="assignedEmployeeId" label={t('assignTo')}>
            <Select allowClear showSearch optionFilterProp="label"
              options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
              style={{ width: '100%' }} popupMatchSelectWidth={false} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={notesTarget?.action === 'complete' ? t('completeTask2') : t('cancelTask2')}
        open={!!notesTarget}
        onOk={handleAction}
        onCancel={() => setNotesTarget(null)}
      >
        <p>{notesTarget?.action === 'complete' ? t('completionNotesOptional') : t('cancelReasonOptional')}</p>
        <Input.TextArea value={notesValue} onChange={(e) => setNotesValue(e.target.value)} rows={3} maxLength={1000} />
      </Modal>
    </Card>
  );
};

export default TasksPage;
