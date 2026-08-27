import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Checkbox, DatePicker, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, message,
} from 'antd';
import { PlusOutlined, PlayCircleOutlined, CheckOutlined, StopOutlined, UndoOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { tasksApi } from '../api/tasks';
import { employeesApi } from '../api/hr';
import { getApiError } from '../api/farmApi';
import type { Employee, FarmTask, FarmTaskPriority, FarmTaskStatus } from '../types';

const PRIORITY_COLORS: Record<FarmTaskPriority, string> = { Low: 'default', Medium: 'blue', High: 'red' };
const STATUS_COLORS: Record<FarmTaskStatus, string> = { Pending: 'gold', InProgress: 'processing', Completed: 'green', Cancelled: 'default' };

const TasksPage: React.FC = () => {
  const [tasks, setTasks] = useState<FarmTask[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
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

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [taskRes, empRes] = await Promise.all([
        tasksApi.list({ ...filters, page: p, pageSize: 10 }),
        employeesApi.list({ page: 1, pageSize: 100 }),
      ]);
      setTasks(taskRes.data.items);
      setTotal(taskRes.data.totalCount);
      setPage(p);
      setEmployees(empRes.data.items);
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
        message.success('Task updated');
      } else {
        await tasksApi.create(data);
        message.success('Task created');
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
      await tasksApi.remove(id);
      message.success('Task deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleAction = async () => {
    if (!notesTarget) return;
    try {
      if (notesTarget.action === 'complete') {
        await tasksApi.complete(notesTarget.id, notesValue || undefined);
        message.success('Task completed');
      } else {
        await tasksApi.cancel(notesTarget.id, notesValue || undefined);
        message.success('Task cancelled');
      }
      setNotesTarget(null);
      setNotesValue('');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FarmTask> = [
    { title: 'Title', dataIndex: 'title', ellipsis: true },
    {
      title: 'Priority',
      dataIndex: 'priority',
      width: 100,
      render: (p: FarmTaskPriority) => <Tag color={PRIORITY_COLORS[p]}>{p}</Tag>,
    },
    {
      title: 'Status',
      dataIndex: 'status',
      width: 110,
      render: (s: FarmTaskStatus) => <Tag color={STATUS_COLORS[s]}>{s}</Tag>,
    },
    {
      title: 'Due',
      dataIndex: 'dueDate',
      render: (d: string, r) => (
        <span style={r.isOverdue ? { color: '#ff4d4f', fontWeight: 600 } : undefined}>
          {dayjs(d).format('YYYY-MM-DD')}
          {r.isOverdue && ' (overdue)'}
        </span>
      ),
    },
    { title: 'Assignee', dataIndex: 'assignedEmployeeName', render: (n?: string) => n ?? '-' },
    {
      title: 'Actions',
      render: (_, r) => (
        <Space size={4}>
          {r.status === 'Pending' && (
            <Button size="small" icon={<PlayCircleOutlined />} onClick={async () => { await tasksApi.start(r.id); load(page); }} />
          )}
          {(r.status === 'Pending' || r.status === 'InProgress') && (
            <>
              <Button size="small" type="primary" icon={<CheckOutlined />}
                onClick={() => setNotesTarget({ id: r.id, action: 'complete' })} />
              <Button size="small" danger icon={<StopOutlined />}
                onClick={() => setNotesTarget({ id: r.id, action: 'cancel' })} />
            </>
          )}
          {(r.status === 'Completed' || r.status === 'Cancelled') && (
            <Button size="small" icon={<UndoOutlined />} onClick={async () => { await tasksApi.reopen(r.id); load(page); }} />
          )}
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Delete?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title="Farm Tasks"
      extra={
        <Space wrap>
          <Select allowClear placeholder="Status" style={{ width: 120 }} value={filters.status}
            onChange={(v) => setFilters((f) => ({ ...f, status: v }))}
            options={['Pending', 'InProgress', 'Completed', 'Cancelled'].map((s) => ({ value: s, label: s }))} />
          <Select allowClear placeholder="Priority" style={{ width: 120 }} value={filters.priority}
            onChange={(v) => setFilters((f) => ({ ...f, priority: v }))}
            options={['Low', 'Medium', 'High'].map((p) => ({ value: p, label: p }))} />
          <Select allowClear placeholder="Assignee" style={{ width: 180 }} value={filters.assignee}
            onChange={(v) => setFilters((f) => ({ ...f, assignee: v }))}
            options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          <Checkbox checked={filters.overdueOnly}
            onChange={(e) => setFilters((f) => ({ ...f, overdueOnly: e.target.checked }))}>
            Overdue only
          </Checkbox>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>New Task</Button>
        </Space>
      }
    >
      <Table
        rowKey="id"
        columns={columns}
        dataSource={tasks}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: load }}
      />

      <Modal title={editing ? 'Edit Task' : 'New Task'} open={modalOpen} onOk={handleSave} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="title" label="Title" rules={[{ required: true }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="priority" label="Priority" rules={[{ required: true }]} style={{ flex: 1 }}>
              <Select options={['Low', 'Medium', 'High'].map((p) => ({ value: p, label: p }))} />
            </Form.Item>
            <Form.Item name="dueDate" label="Due Date" rules={[{ required: true }]} style={{ flex: 1 }}>
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Form.Item name="assignedEmployeeId" label="Assign to">
            <Select allowClear showSearch optionFilterProp="label"
              options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={notesTarget?.action === 'complete' ? 'Complete Task' : 'Cancel Task'}
        open={!!notesTarget}
        onOk={handleAction}
        onCancel={() => setNotesTarget(null)}
      >
        <p>{notesTarget?.action === 'complete' ? 'Completion notes (optional):' : 'Cancel reason (optional):'}</p>
        <Input.TextArea value={notesValue} onChange={(e) => setNotesValue(e.target.value)} rows={3} maxLength={1000} />
      </Modal>
    </Card>
  );
};

export default TasksPage;
