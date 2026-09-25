import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, Modal, Popconfirm, Row, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { departmentsApi, employeeRolesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { Department, EmployeeRole } from '../../types';
import { useTranslation } from 'react-i18next';

const DepartmentsRolesPage: React.FC = () => {const { t } = useTranslation('hr'); 
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
      message.success(t('departmentCreated'));
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
      message.success(t('departmentDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleCreateRole = async () => {
    try {
      const values = await roleForm.validateFields();
      await employeeRolesApi.create(values);
      message.success(t('roleCreated'));
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
      message.success(t('roleDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  // Description gives way on a phone (see finance/CategoriesPage): it is the widest column and
  // the least critical, so hiding it below 768px lets Name + Employees + Delete fit a stacked
  // full-width card without horizontal scrolling. It returns from md up.
  const deptColumns: ColumnsType<Department> = [
    { title: t('name'), dataIndex: 'name' },
    { title: t('description'), dataIndex: 'description', render: (d?: string) => d ?? '-', responsive: ['md'] },
    { title: t('employees'), dataIndex: 'employeeCount', align: 'center' },
    {
      title: '',
      render: (_, r) => (
        <Popconfirm title={t('deleteThisDepartment')} onConfirm={() => handleDeleteDept(r.id)}>
          <Button size="small" danger>{t('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const roleColumns: ColumnsType<EmployeeRole> = [
    { title: t('name'), dataIndex: 'name' },
    { title: t('description'), dataIndex: 'description', render: (d?: string) => d ?? '-', responsive: ['md'] },
    { title: t('employees'), dataIndex: 'employeeCount', align: 'center' },
    {
      title: '',
      render: (_, r) => (
        <Popconfirm title={t('deleteThisRole')} onConfirm={() => handleDeleteRole(r.id)}>
          <Button size="small" danger>{t('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} md={12}>
        <Card
          title={t('departments')}
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => { deptForm.resetFields(); setDeptModal(true); }}>
              {t('add')}
            </Button>
          }
        >
          <Table rowKey="id" columns={deptColumns} dataSource={departments} loading={loading} pagination={false} size="small" />
        </Card>
      </Col>
      <Col xs={24} md={12}>
        <Card
          title={t('employeeRoles')}
          extra={
            <Button size="small" icon={<PlusOutlined />} onClick={() => { roleForm.resetFields(); setRoleModal(true); }}>
              {t('add')}
            </Button>
          }
        >
          <Table rowKey="id" columns={roleColumns} dataSource={roles} loading={loading} pagination={false} size="small" />
        </Card>
      </Col>

      <Modal title={t('newDepartment')} open={deptModal} onOk={handleCreateDept} onCancel={() => setDeptModal(false)} destroyOnClose>
        <Form form={deptForm} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal title={t('newRole')} open={roleModal} onOk={handleCreateRole} onCancel={() => setRoleModal(false)} destroyOnClose>
        <Form form={roleForm} layout="vertical">
          <Form.Item name="name" label={t('name')} rules={[{ required: true }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="description" label={t('description')}>
            <Input maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Row>
  );
};

export default DepartmentsRolesPage;
