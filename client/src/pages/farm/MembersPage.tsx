import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { membersApi, FARM_ROLES, type FarmInvitation, type FarmMember } from '../../api/members';
import { getApiError } from '../../api/farmApi';

const { Text } = Typography;

const roleOptions = FARM_ROLES.map((role) => ({ value: role, label: role }));

const roleColor = (role: string): string => {
  switch (role) {
    case 'SystemOwner': return 'purple';
    case 'FarmManager': return 'blue';
    case 'Veterinarian': return 'green';
    case 'Accountant': return 'gold';
    case 'Viewer': return 'default';
    default: return 'default';
  }
};

const MembersPage: React.FC = () => {
  const [members, setMembers] = useState<FarmMember[]>([]);
  const [invitations, setInvitations] = useState<FarmInvitation[]>([]);
  const [loading, setLoading] = useState(false);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ email: string; role: string }>();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [membersRes, invitationsRes] = await Promise.all([
        membersApi.list(),
        membersApi.listInvitations(),
      ]);
      setMembers(membersRes.data);
      setInvitations(invitationsRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const handleInvite = async (values: { email: string; role: string }) => {
    setSaving(true);
    try {
      await membersApi.invite(values.email.trim(), values.role);
      message.success(`Invitation sent to ${values.email.trim()}`);
      setInviteOpen(false);
      form.resetFields();
      await load();
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setSaving(false);
    }
  };

  const handleRoleChange = async (userId: string, role: string) => {
    try {
      await membersApi.updateRole(userId, role);
      message.success('Role updated');
      await load();
    } catch (err) {
      message.error(getApiError(err));
      await load(); // put the select back to the server's truth
    }
  };

  const handleRemove = async (userId: string) => {
    try {
      await membersApi.remove(userId);
      message.success('Member removed');
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleRevoke = async (id: string) => {
    try {
      await membersApi.revokeInvitation(id);
      message.success('Invitation revoked');
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const memberColumns: ColumnsType<FarmMember> = [
    {
      title: 'Member',
      key: 'member',
      render: (_, m) => (
        <Space direction="vertical" size={0}>
          <Text strong>{`${m.firstName} ${m.lastName}`.trim() || m.email}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{m.email}</Text>
        </Space>
      ),
    },
    {
      title: 'Farm role',
      dataIndex: 'role',
      width: 220,
      render: (role: string, m) => (
        <Space>
          <Tag color={roleColor(role)}>{role}</Tag>
          <Select
            aria-label={`Change role for ${m.email}`}
            size="small"
            value={role}
            options={roleOptions}
            style={{ width: 150 }}
            onChange={(value) => void handleRoleChange(m.userId, value)}
          />
        </Space>
      ),
    },
    {
      title: 'Joined',
      dataIndex: 'joinedAt',
      width: 140,
      render: (d: string) => (d ? dayjs(d).format('YYYY-MM-DD') : '-'),
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 120,
      render: (_, m) => (
        <Popconfirm
          title="Remove this member?"
          description="They will lose access to this farm immediately."
          okText="Remove"
          okButtonProps={{ danger: true }}
          onConfirm={() => void handleRemove(m.userId)}
        >
          <Button danger size="small">Remove</Button>
        </Popconfirm>
      ),
    },
  ];

  const invitationColumns: ColumnsType<FarmInvitation> = [
    { title: 'Email', dataIndex: 'email', ellipsis: true },
    {
      title: 'Role',
      dataIndex: 'role',
      width: 160,
      render: (role: string) => <Tag color={roleColor(role)}>{role}</Tag>,
    },
    {
      title: 'Expires',
      dataIndex: 'expiresAt',
      width: 140,
      render: (d: string) => (d ? dayjs(d).format('YYYY-MM-DD') : '-'),
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 120,
      render: (_, i) => (
        <Button size="small" onClick={() => void handleRevoke(i.id)}>Revoke</Button>
      ),
    },
  ];

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Card
        title="Farm members"
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} onClick={() => void load()}>Refresh</Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setInviteOpen(true)}>
              Invite member
            </Button>
          </Space>
        }
      >
        <Table
          rowKey="userId"
          columns={memberColumns}
          dataSource={members}
          loading={loading}
          pagination={false}
        />
      </Card>

      <Card title="Pending invitations">
        <Table
          rowKey="id"
          columns={invitationColumns}
          dataSource={invitations}
          loading={loading}
          pagination={false}
          locale={{ emptyText: 'No pending invitations.' }}
        />
      </Card>

      <Modal
        title="Invite a member"
        open={inviteOpen}
        onCancel={() => setInviteOpen(false)}
        onOk={() => form.submit()}
        confirmLoading={saving}
        okText="Send invitation"
        destroyOnClose
      >
        <Form form={form} layout="vertical" onFinish={handleInvite} initialValues={{ role: 'Viewer' }}>
          <Form.Item
            name="email"
            label="Email address"
            rules={[
              { required: true, message: 'Please enter an email address' },
              { type: 'email', message: 'Please enter a valid email address' },
            ]}
          >
            <Input placeholder="person@example.com" />
          </Form.Item>
          <Form.Item
            name="role"
            label="Farm role"
            rules={[{ required: true, message: 'Please choose a role' }]}
          >
            <Select options={roleOptions} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
};

export default MembersPage;
