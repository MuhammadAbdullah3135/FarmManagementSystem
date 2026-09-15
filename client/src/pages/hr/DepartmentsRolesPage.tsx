import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, Modal, Popconfirm, Row, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { departmentsApi, employeeRolesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { Department, EmployeeRole } from '../../types';

const DepartmentsRolesPage: React.FC = () => {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [roles, setRoles] = useState<EmployeeRole[]>([]);
  const [loading, setLoading] = useState(false);
  const [deptModal, setDeptModal] = useState(false);
  const [roleModal, setRoleModal] = useState(false);
  const [deptForm] = Form.useForm();
  const [roleForm] = Form.useForm();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [d, r] = await Promise.all([departmentsApi.list(), employeeRolesApi.list()]);
      setDepartments(d.data);
      setRoles(r.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const handleCreateDept = async () => {
    try {
      const values = await deptForm.validateFields();
      await departmentsApi.create(values);
      message.success('Department created');
      setDeptModal(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDeleteDept = async (id: string) => {
    try {
      await departmentsApi.remove(id);
      message.success('Department deleted');
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleCreateRole = async () => {
    try {
      const values = await roleForm.validateFields();
      await employeeRolesApi.create(values);
      message.success('Role created');
      setRoleModal(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDeleteRole = async (id: string) => {
    try {
      await employeeRolesApi.remove(id);
      message.success('Role deleted');
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const deptColumns: ColumnsType<Department> = [
    { title: 'Name', dataIndex: 'name' },
    { title: 'Description', dataIndex: 'description', render: (d?: string) => d ?? '-' },
    { title: 'Employees', dataIndex: 'employeeCount', align: 'center' },
    {
      title: '',
      render: (_, r) => (
        <Popconfirm title="Delete this department?" onConfirm={() => handleDeleteDept(r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const roleColumns: ColumnsType<EmployeeRole> = [
    { title: 'Name', dataIndex: 'name' },
    { title: 'Description', dataIndex: 'description', render: (d?: string) => d ?? '-' },
    { title: 'Employees', dataIndex: 'employeeCount', align: 'center' },
    {
      title: '',
      render: (_, r) => (
        <Popconfirm title="Delete this role?" onConfirm={() => handleDeleteRole(r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <Row gutter={16}>
      <Col xs={24} md={12}>
        <Card
          title="Departments"
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => { deptForm.resetFields(); setDeptModal(true); }}>
              Add
            </Button>
          }
        >
          <Table rowKey="id" columns={deptColumns} dataSource={departments} loading={loading} pagination={false} size="small" />
        </Card>
      </Col>
      <Col xs={24} md={12}>
        <Card
          title="Employee Roles"
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => { roleForm.resetFields(); setRoleModal(true); }}>
              Add
            </Button>
          }
        >
          <Table rowKey="id" columns={roleColumns} dataSource={roles} loading={loading} pagination={false} size="small" />
        </Card>
      </Col>

      <Modal title="New Department" open={deptModal} onOk={handleCreateDept} onCancel={() => setDeptModal(false)} destroyOnClose>
        <Form form={deptForm} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal title="New Role" open={roleModal} onOk={handleCreateRole} onCancel={() => setRoleModal(false)} destroyOnClose>
        <Form form={roleForm} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="description" label="Description">
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Row>
  );
};

export default DepartmentsRolesPage;
