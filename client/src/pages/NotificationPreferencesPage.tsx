import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Space, Switch, Table, Tag, Typography, message,
} from 'antd';
import { ArrowLeftOutlined, SaveOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { notificationsApi, alertTypeLabel } from '../api/notifications';
import type { NotificationPreference } from '../api/notifications';
import { getApiError } from '../api/farmApi';
import { useFarmStore } from '../stores/farmStore';

const { Title, Paragraph, Text } = Typography;

/**
 * Per-alert-type channel choices, so the notification system stays useful instead
 * of becoming background noise.
 *
 * The server owns the alert-type vocabulary and the defaults: this page renders
 * whatever matrix it is handed, so a new alert type appears here without a client
 * change and the toggles always show the values the dispatcher will actually use.
 *
 * One row per alert type per farm — a user can care about breeding on one farm and
 * not another, and shared staff accounts would otherwise need a global setting.
 */
const NotificationPreferencesPage: React.FC = () => {
  const navigate = useNavigate();
  const { activeFarm } = useFarmStore();

  const [preferences, setPreferences] = useState<NotificationPreference[]>([]);
  const [minEmailSeverity, setMinEmailSeverity] = useState('Critical');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);

  const load = useCallback(async () => {
    if (!activeFarm) return;
    setLoading(true);
    try {
      const response = await notificationsApi.getPreferences();
      setPreferences(response.data.preferences);
      setMinEmailSeverity(response.data.emailMinSeverityOnByDefault);
      setDirty(false);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [activeFarm]);

  useEffect(() => {
    void load();
  }, [load]);

  const update = (alertType: string, patch: Partial<NotificationPreference>) => {
    setPreferences((current) =>
      current.map((p) => (p.alertType === alertType ? { ...p, ...patch } : p)),
    );
    setDirty(true);
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      const response = await notificationsApi.updatePreferences(preferences);
      setPreferences(response.data.preferences);
      setDirty(false);
      message.success('Notification preferences saved.');
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setSaving(false);
    }
  };

  const columns: ColumnsType<NotificationPreference> = [
    {
      title: 'Alert type',
      dataIndex: 'alertType',
      key: 'alertType',
      render: (alertType: string) => alertTypeLabel(alertType),
    },
    {
      title: 'In-app',
      dataIndex: 'inAppEnabled',
      key: 'inAppEnabled',
      width: 120,
      render: (enabled: boolean, record) => (
        <Switch
          aria-label={`In-app notifications for ${alertTypeLabel(record.alertType)}`}
          checked={enabled}
          onChange={(checked) => update(record.alertType, { inAppEnabled: checked })}
        />
      ),
    },
    {
      title: 'Email',
      dataIndex: 'emailEnabled',
      key: 'emailEnabled',
      width: 120,
      render: (enabled: boolean, record) => (
        <Switch
          aria-label={`Email notifications for ${alertTypeLabel(record.alertType)}`}
          checked={enabled}
          onChange={(checked) => update(record.alertType, { emailEnabled: checked })}
        />
      ),
    },
  ];

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
        <div>
          <Title level={3} style={{ marginBottom: 0 }}>Notification Preferences</Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Choose which alerts reach you for {activeFarm?.name ?? 'this farm'}, and on which channel.
            These settings apply to your account only.
          </Paragraph>
        </div>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/dashboard/notifications')}>
            Back
          </Button>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            loading={saving}
            disabled={!dirty}
            onClick={() => void handleSave()}
          >
            Save
          </Button>
        </Space>
      </Space>

      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        title="Email is reserved for what matters by default"
        description={
          <>
            Alerts at <Tag color="red">{minEmailSeverity}</Tag> severity or above are emailed by default,
            because emailing everything is how a channel gets ignored. Turn email on per alert type below
            if you want more, and off entirely if you would rather just use the notification center.
            Push and SMS delivery are not implemented yet.
          </>
        }
      />

      <Card extra={<Text type="secondary">{preferences.length} alert type(s)</Text>}>
        <Table<NotificationPreference>
          rowKey="alertType"
          size="small"
          loading={loading}
          columns={columns}
          dataSource={preferences}
          pagination={false}
        />
      </Card>
    </div>
  );
};

export default NotificationPreferencesPage;
