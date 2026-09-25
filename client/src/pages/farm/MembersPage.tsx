import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { formatDate } from '../../i18n/format';

import { membersApi, FARM_ROLES, type FarmInvitation, type FarmMember } from '../../api/members';
import { farmRoleLabel, labeledOptions } from '../../i18n/vocabulary';
import type { TFunction } from 'i18next';
import { getApiError } from '../../api/farmApi';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

// The value stays the stored role name — it is what the API is sent and what the permission
// tables compare against. Only the label the user reads is translated.
const roleOptions = (t: TFunction) =>
  labeledOptions(FARM_ROLES, farmRoleLabel, t);

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

const MembersPage: React.FC = () => {const { t } = useTranslation('common'); 
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
      message.success(t('roleUpdated'));
      await load();
    } catch (err) {
      message.error(getApiError(err));
      await load(); // put the select back to the server's truth
    }
  };

  const handleRemove = async (userId: string) => {
    try {
      await membersApi.remove(userId);
      message.success(t('memberRemoved'));
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleRevoke = async (id: string) => {
    try {
      await membersApi.revokeInvitation(id);
      message.success(t('invitationRevoked'));
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const memberColumns: ColumnsType<FarmMember> = [
    {
      title: t('member'),
      key: 'member',
      render: (_, m) => (
        <Space direction="vertical" size={0}>
          <Text strong>{`${m.firstName} ${m.lastName}`.trim() || m.email}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{m.email}</Text>
        </Space>
      ),
    },
    {
      title: t('farmRole'),
      dataIndex: 'role',
      width: 220,
      render: (role: string, m) => (
        <Space>
          <Tag color={roleColor(role)}>{farmRoleLabel(t, role)}</Tag>
          <Select
            aria-label={t('changeRoleFor', { email: m.email })}
            size="small"
            value={role}
            options={roleOptions(t)}
            style={{ width: 150 }}
            onChange={(value) => void handleRoleChange(m.userId, value)}
          />
        </Space>
      ),
    },
    {
      title: t('joined'),
      dataIndex: 'joinedAt',
      width: 140,
      render: (d: string) => (d ? formatDate(d) : '-'),
    },
    {
      title: t('actions'),
      key: 'actions',
      width: 120,
      render: (_, m) => (
        <Popconfirm
          title={t('removeThisMember')}
          description={t('theyWillLoseAccessToThisFarmImmediately')}
          okText={t('remove')}
          okButtonProps={{ danger: true }}
          onConfirm={() => void handleRemove(m.userId)}
        >
          <Button danger size="small">{t('remove')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const invitationColumns: ColumnsType<FarmInvitation> = [
    { title: t('email'), dataIndex: 'email', ellipsis: true },
    {
      title: t('role'),
      dataIndex: 'role',
      width: 160,
      render: (role: string) => <Tag color={roleColor(role)}>{farmRoleLabel(t, role)}</Tag>,
    },
    {
      title: t('expires'),
      dataIndex: 'expiresAt',
      width: 140,
      render: (d: string) => (d ? formatDate(d) : '-'),
    },
    {
      title: t('actions'),
      key: 'actions',
      width: 120,
      render: (_, i) => (
        <Button size="small" onClick={() => void handleRevoke(i.id)}>{t('revoke')}</Button>
      ),
    },
  ];

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Card
        title={t('farmMembers')}
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} onClick={() => void load()}>{t('refresh')}</Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setInviteOpen(true)}>
              {t('inviteMember')}
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

      <Card title={t('pendingInvitations')}>
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
        title={t('inviteAMember')}
        open={inviteOpen}
        onCancel={() => setInviteOpen(false)}
        onOk={() => form.submit()}
        confirmLoading={saving}
        okText={t('sendInvitation')}
        destroyOnClose
      >
        <Form form={form} layout="vertical" onFinish={handleInvite} initialValues={{ role: 'Viewer' }}>
          <Form.Item
            name="email"
            label={t('emailAddress')}
            rules={[
              { required: true, message: 'Please enter an email address' },
              { type: 'email', message: 'Please enter a valid email address' },
            ]}
          >
            <Input placeholder={t('personExampleCom')} />
          </Form.Item>
          <Form.Item
            name="role"
            label={t('farmRole')}
            rules={[{ required: true, message: 'Please choose a role' }]}
          >
            <Select options={roleOptions(t)} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
};

export default MembersPage;
