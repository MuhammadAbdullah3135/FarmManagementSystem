import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, Modal, Select, Space, Table, Tag, message,
} from 'antd';
import { LoginOutlined, LogoutOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { attendanceApi, lookupsApi } from '../../api/attendance';
import { employeesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { AttendanceRecord, AttendanceStatus, Employee } from '../../types';

const STATUS_COLORS: Record<AttendanceStatus, string> = {
  Present: 'green',
  Absent: 'red',
  Late: 'orange',
  HalfDay: 'blue',
  Leave: 'purple',
  Holiday: 'cyan',
};

const ATTENDANCE_STATUSES: AttendanceStatus[] = ['Present', 'Absent', 'Late', 'HalfDay', 'Leave', 'Holiday'];

const AttendancePage: React.FC = () => {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null]>([dayjs(), dayjs()]);
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();

  const loadEmployees = useCallback(async () => {
    try {
      const res = await employeesApi.list({ page: 1, pageSize: 100 });
      setEmployees(res.data.items);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const loadRecords = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await attendanceApi.list({
        from: dateRange[0]?.toISOString(),
        to: dateRange[1]?.toISOString(),
        page: p,
        pageSize: 10,
      });
      setRecords(res.data.items);
      setTotal(res.data.totalCount);
      setPage(p);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [dateRange]);

  useEffect(() => {
    loadEmployees();
  }, [loadEmployees]);

  useEffect(() => {
    loadRecords(1);
  }, [loadRecords]);

  const handleCheckIn = async (empId: string) => {
    try {
      await attendanceApi.checkIn(empId);
      message.success('Checked in');
      loadRecords(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleCheckOut = async (empId: string) => {
    try {
      await attendanceApi.checkOut(empId);
      message.success('Checked out');
      loadRecords(page);
    } catch (err) {
      message.error(getApiError(err));
    }
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
      message.success('Attendance recorded');
      setModalOpen(false);
      loadRecords(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await attendanceApi.remove(id);
      message.success('Record deleted');
      loadRecords(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<AttendanceRecord> = [
    { title: 'Employee', dataIndex: 'employeeName' },
    { title: 'Date', dataIndex: 'date', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    {
      title: 'Status',
      dataIndex: 'status',
      render: (s: AttendanceStatus) => <Tag color={STATUS_COLORS[s]}>{s}</Tag>,
    },
    {
      title: 'Check In',
      dataIndex: 'checkInAt',
      render: (d?: string) => d ? dayjs(d).format('HH:mm') : '-',
    },
    {
      title: 'Check Out',
      dataIndex: 'checkOutAt',
      render: (d?: string) => d ? dayjs(d).format('HH:mm') : '-',
    },
    {
      title: 'Hours',
      dataIndex: 'hoursWorked',
      render: (h?: number) => h != null ? `${h}h` : '-',
    },
    { title: 'Notes', dataIndex: 'notes', ellipsis: true, render: (n?: string) => n ?? '-' },
    {
      title: 'Actions',
      render: (_, r) => (
        <Space>
          <Button size="small" danger onClick={() => handleDelete(r.id)}>Delete</Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Card title="Today's Attendance" style={{ marginBottom: 16 }}>
        <Space wrap>
          {employees.filter((e) => e.isActive).map((emp) => (
            <Card key={emp.id} size="small" style={{ width: 200 }}>
              <div style={{ marginBottom: 8 }}>{emp.firstName} {emp.lastName}</div>
              <Space>
                <Button size="small" type="primary" icon={<LoginOutlined />} onClick={() => handleCheckIn(emp.id)}>
                  In
                </Button>
                <Button size="small" icon={<LogoutOutlined />} onClick={() => handleCheckOut(emp.id)}>
                  Out
                </Button>
              </Space>
            </Card>
          ))}
        </Space>
      </Card>

      <Card
        title="Attendance Records"
        extra={
          <Space>
            <DatePicker.RangePicker value={dateRange} onChange={(v) => v && setDateRange(v)} />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); form.setFieldsValue({ date: dayjs(), status: 'Present' }); setModalOpen(true); }}>
              Manual Entry
            </Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={records}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: loadRecords }}
        />
      </Card>

      <Modal title="Manual Attendance" open={modalOpen} onOk={handleUpsert} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="employeeId" label="Employee" rules={[{ required: true }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
            />
          </Form.Item>
          <Form.Item name="date" label="Date" rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="status" label="Status" rules={[{ required: true }]}>
            <Select options={ATTENDANCE_STATUSES.map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="checkInAt" label="Check In" style={{ flex: 1 }}>
              <DatePicker showTime style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="checkOutAt" label="Check Out" style={{ flex: 1 }}>
              <DatePicker showTime style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default AttendancePage;
