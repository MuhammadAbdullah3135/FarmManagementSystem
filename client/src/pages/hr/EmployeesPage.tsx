import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined, SearchOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { employeesApi, departmentsApi, employeeRolesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { Department, Employee, EmployeeRole } from '../../types';

const SALARY_TYPES = ['Monthly', 'Weekly', 'Daily', 'Hourly'];

const EmployeesPage: React.FC = () => {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [roles, setRoles] = useState<EmployeeRole[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  const [search, setSearch] = useState('');
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [empRes, deptRes, roleRes] = await Promise.all([
        employeesApi.list({ search: search || undefined, page: p, pageSize: 10 }),
        departmentsApi.list(),
        employeeRolesApi.list(),
      ]);
      setEmployees(empRes.data.items);
      setTotal(empRes.data.totalCount);
      setPage(p);
      setDepartments(deptRes.data);
      setRoles(roleRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => { load(1); }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ salaryType: 'Monthly', isActive: true });
    setModalOpen(true);
  };

  const openEdit = (emp: Employee) => {
    setEditing(emp);
    form.setFieldsValue({
      firstName: emp.firstName,
      lastName: emp.lastName,
      phone: emp.phone,
      email: emp.email,
      departmentId: emp.departmentId,
      employeeRoleId: emp.employeeRoleId,
      salaryType: emp.salaryType,
      salaryRate: emp.salaryRate,
      isActive: emp.isActive,
      notes: emp.notes,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await employeesApi.update(editing.id, { ...values, hireDate: editing.hireDate });
        message.success('Employee updated');
      } else {
        await employeesApi.create({
          ...values,
          hireDate: dayjs().format('YYYY-MM-DD'),
        });
        message.success('Employee created');
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
      await employeesApi.remove(id);
      message.success('Employee deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<Employee> = [
    {
      title: 'Name',
      render: (_, r) => <span>{r.firstName} {r.lastName}</span>,
    },
    { title: 'Phone', dataIndex: 'phone', render: (p?: string) => p ?? '-' },
    { title: 'Email', dataIndex: 'email', render: (e?: string) => e ?? '-' },
    { title: 'Department', dataIndex: 'departmentName', render: (d?: string) => d ?? '-' },
    { title: 'Role', dataIndex: 'employeeRoleName', render: (r?: string) => r ?? '-' },
    {
      title: 'Salary',
      render: (_, r) => `${r.salaryTypeName} ${r.salaryRate.toLocaleString()}`,
    },
    { title: 'Hire Date', dataIndex: 'hireDate', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    {
      title: 'Active',
      dataIndex: 'isActive',
      render: (a: boolean) => a ? <Tag color="green">Active</Tag> : <Tag>Inactive</Tag>,
    },
    {
      title: 'Actions',
      render: (_, r) => (
        <Space>
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Soft-delete this employee?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title="Employees"
      extra={
        <Space>
          <Input
            placeholder="Search..."
            prefix={<SearchOutlined />}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onPressEnter={() => load(1)}
            style={{ width: 200 }}
          />
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Add Employee
          </Button>
        </Space>
      }
    >
      <Table
        rowKey="id"
        columns={columns}
        dataSource={employees}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: load }}
      />

      <Modal
        title={editing ? 'Edit Employee' : 'Add Employee'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={640}
      >
        <Form form={form} layout="vertical">
          <Space style={{ display: 'flex' }}>
            <Form.Item name="firstName" label="First Name" rules={[{ required: true }]} style={{ flex: 1 }}>
              <Input maxLength={100} />
            </Form.Item>
            <Form.Item name="lastName" label="Last Name" rules={[{ required: true }]} style={{ flex: 1 }}>
              <Input maxLength={100} />
            </Form.Item>
          </Space>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="phone" label="Phone" style={{ flex: 1 }}>
              <Input maxLength={50} />
            </Form.Item>
            <Form.Item name="email" label="Email" style={{ flex: 1 }}>
              <Input maxLength={200} />
            </Form.Item>
          </Space>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="departmentId" label="Department" style={{ flex: 1 }}>
              <Select allowClear options={departments.map((d) => ({ value: d.id, label: d.name }))} />
            </Form.Item>
            <Form.Item name="employeeRoleId" label="Role" style={{ flex: 1 }}>
              <Select allowClear options={roles.map((r) => ({ value: r.id, label: r.name }))} />
            </Form.Item>
          </Space>
          <Space style={{ display: 'flex' }}>
            <Form.Item name="salaryType" label="Salary Type" rules={[{ required: true }]} style={{ flex: 1 }}>
              <Select options={SALARY_TYPES.map((t) => ({ value: t, label: t }))} />
            </Form.Item>
            <Form.Item name="salaryRate" label="Salary Rate" rules={[{ required: true }]} style={{ flex: 1 }}>
              <InputNumber min={0} style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          {editing && (
            <Form.Item name="isActive" label="Active" valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default EmployeesPage;
