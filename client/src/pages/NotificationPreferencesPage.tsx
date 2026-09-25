import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Space, Switch, Table, Tag, Typography, message,
} from 'antd';
import { SaveOutlined } from '@ant-design/icons';
import { DirectionalIcon } from '../i18n/DirectionalIcon';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { notificationsApi } from '../api/notifications';
import { alertTypeLabel, severityLabel } from '../i18n/vocabulary';
import type { NotificationPreference } from '../api/notifications';
import { getApiError } from '../api/farmApi';
import { useFarmStore } from '../stores/farmStore';
import { useTranslation } from 'react-i18next';

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
const NotificationPreferencesPage: React.FC = () => {const { t } = useTranslation('notifications'); 
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
      message.success(t('notificationPreferencesSaved'));
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setSaving(false);
    }
  };

  const columns: ColumnsType<NotificationPreference> = [
    {
      title: t('alertType'),
      dataIndex: 'alertType',
      key: 'alertType',
      render: (alertType: string) => alertTypeLabel(t, alertType),
    },
    {
      title: t('inApp'),
      dataIndex: 'inAppEnabled',
      key: 'inAppEnabled',
      width: 120,
      render: (enabled: boolean, record) => (
        <Switch
          aria-label={t('ariaInAppNotificationsFor', { alertType: alertTypeLabel(t, record.alertType) })}
          checked={enabled}
          onChange={(checked) => update(record.alertType, { inAppEnabled: checked })}
        />
      ),
    },
    {
      title: t('email'),
      dataIndex: 'emailEnabled',
      key: 'emailEnabled',
      width: 120,
      render: (enabled: boolean, record) => (
        <Switch
          aria-label={t('ariaEmailNotificationsFor', { alertType: alertTypeLabel(t, record.alertType) })}
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
          <Title level={3} style={{ marginBottom: 0 }}>{t('notificationPreferences')}</Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('chooseWhichAlertsReachYouFor')} {activeFarm?.name ?? 'this farm'}{t('andOnWhichChannelTheseSettingsApplyTo')}
          </Paragraph>
        </div>
        <Space>
          <Button icon={<DirectionalIcon role="back" />} onClick={() => navigate('/dashboard/notifications')}>
            {t('back')}
          </Button>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            loading={saving}
            disabled={!dirty}
            onClick={() => void handleSave()}
          >
            {t('save')}
          </Button>
        </Space>
      </Space>

      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        title={t('emailIsReservedForWhatMattersByDefault')}
        description={
          <>
            {t('alertsAt')} <Tag color="red">{severityLabel(t, minEmailSeverity)}</Tag> {t('severityOrAboveAreEmailedByDefaultBecause')}
          </>
        }
      />

      <Card extra={<Text type="secondary">{preferences.length} {t('alertTypeS')}</Text>}>
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
