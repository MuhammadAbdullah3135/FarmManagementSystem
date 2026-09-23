import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { employeesApi, departmentsApi, employeeRolesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import { useCachedQuery } from '../../offline/cachedQuery';
import SyncAgeLabel from '../../components/SyncAgeLabel';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { Department, Employee, EmployeeRole } from '../../types';

const SALARY_TYPES = ['Monthly', 'Weekly', 'Daily', 'Hourly'];

const EmployeesPage: React.FC = () => {
  const navigate = useNavigate();
  const [departments, setDepartments] = useState<Department[]>([]);
  const [roles, setRoles] = useState<EmployeeRole[]>([]);
  const [page, setPage] = useState(1);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  // The input's text versus the search the list was actually fetched with: the table
  // reloads on Enter (as it always has), not on every keystroke.
  const [search, setSearch] = useState('');
  const [submittedSearch, setSubmittedSearch] = useState('');
  const [form] = Form.useForm();

  /**
   * Page one with no search is the cached roster (Phase 5.2). Searches and later pages
   * are live queries and are deliberately never written to the device — otherwise every
   * keystroke would leave another copy of the employee list on it.
   */
  const employeesQuery = useCachedQuery<Employee>({
    collection: 'employees',
    variant: 'listPage1',
    // Delta-capable, but only the cached view uses it: a search or a later page is a live query
    // that reads straight from the server and stores nothing.
    supportsDelta: true,
    cacheable: page === 1 && !submittedSearch,
    paramsKey: `${page}|${submittedSearch}`,
    fetcher: async (cursor) => {
      const res = await employeesApi.list({
        search: submittedSearch || undefined,
        page,
        pageSize: 10,
        updatedSince: cursor,
      });
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

  /** Keeps the input, the page and the submitted search in step without a reload loop. */
  const handleSearch = () => {
    if (page === 1 && search === submittedSearch) {
      // Re-running the same search should still re-ask the server.
      employeesQuery.refresh();
      return;
    }
    setPage(1);
    setSubmittedSearch(search);
  };

  /** Options-only refresh: keeps the table page and search untouched. */
  const loadOptions = useCallback(async () => {
    try {
      const [deptRes, roleRes] = await Promise.all([
        departmentsApi.list(),
        employeeRolesApi.list(),
      ]);
      setDepartments(deptRes.data);
      setRoles(roleRes.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  // Departments and roles are lookups the form needs, not cached work lists: they are
  // out of this subphase's scope and stay network-only.
  useEffect(() => {
    const timer = window.setTimeout(() => { void loadOptions(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadOptions]);

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
      employeesQuery.refresh();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await employeesApi.remove(id);
      message.success('Employee deleted');
      employeesQuery.refresh();
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
            onPressEnter={handleSearch}
            style={{ width: 200 }}
          />
          <Button icon={<UploadOutlined />} onClick={() => navigate('/dashboard/hr/employees/import')}>
            Import
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Add Employee
          </Button>
        </Space>
      }
    >
      {/* The freshness label describes these rows: cached views must never look live. */}
      {employeesQuery.lastSyncedAt && (
        <div style={{ marginBottom: 8 }}>
          <SyncAgeLabel lastSyncedAt={employeesQuery.lastSyncedAt} />
        </div>
      )}
      <Table
        rowKey="id"
        columns={columns}
        dataSource={employeesQuery.rows}
        loading={employeesQuery.isLoading}
        pagination={{ current: page, total: employeesQuery.total, pageSize: 10, onChange: setPage }}
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
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="firstName" label="First Name" rules={[{ required: true }]}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="lastName" label="Last Name" rules={[{ required: true }]}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="phone" label="Phone">
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="email" label="Email">
                <Input maxLength={200} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="departmentId" label="Department">
                <LookupQuickAddSelect
                  kind="department"
                  allowClear
                  options={departments.map((d) => ({ value: d.id, label: d.name }))}
                  onCreated={() => loadOptions()}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="employeeRoleId" label="Role">
                <LookupQuickAddSelect
                  kind="employeeRole"
                  allowClear
                  options={roles.map((r) => ({ value: r.id, label: r.name }))}
                  onCreated={() => loadOptions()}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="salaryType" label="Salary Type" rules={[{ required: true }]}>
                <Select options={SALARY_TYPES.map((t) => ({ value: t, label: t }))} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="salaryRate" label="Salary Rate" rules={[{ required: true }]}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
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
