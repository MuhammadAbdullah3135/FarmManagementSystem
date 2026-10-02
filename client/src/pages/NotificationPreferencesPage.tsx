import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Space, Switch, Table, Tag, Typography, message,
} from 'antd';
import { BellOutlined, SaveOutlined } from '@ant-design/icons';
import { DirectionalIcon } from '../i18n/DirectionalIcon';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { notificationsApi } from '../api/notifications';
import { alertTypeLabel, severityLabel } from '../i18n/vocabulary';
import type { NotificationPreference, PushSettings, PushSubscriptionSummary } from '../api/notifications';
import { getApiError } from '../api/farmApi';
import { browserPush, disablePush, enablePush, pushState } from '../push/pushSubscribe';
import type { PushState } from '../push/pushSubscribe';
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
 *
 * <p>
 * Push is the third channel and the only one that needs a device: the column silences
 * an alert type on every registered device, while the card above it decides whether this
 * browser is one of them. Both halves are needed for anything to arrive, which is why the
 * card says which half is missing rather than showing a switch that would do nothing.
 * </p>
 */
const NotificationPreferencesPage: React.FC = () => {const { t } = useTranslation('notifications'); 
  const navigate = useNavigate();
  const { activeFarm } = useFarmStore();

  const [preferences, setPreferences] = useState<NotificationPreference[]>([]);
  const [minEmailSeverity, setMinEmailSeverity] = useState('Critical');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);

  const [pushSettings, setPushSettings] = useState<PushSettings | null>(null);
  const [subscriptionEndpoint, setSubscriptionEndpoint] = useState<string | null>(null);
  const [permission, setPermission] = useState<NotificationPermission | 'unsupported'>('default');
  const [pushBusy, setPushBusy] = useState(false);

  /**
   * What this browser can actually do, read separately from what the server offers.
   *
   * `Notification.permission` and the browser's own subscription are the only sources for
   * these, and a browser that has one but not the other is the case this card exists to
   * make visible.
   */
  const readBrowserPush = useCallback(async () => {
    if (!browserPush.isSupported()) {
      setPermission('unsupported');
      setSubscriptionEndpoint(null);
      return;
    }

    setPermission(browserPush.permission());

    try {
      setSubscriptionEndpoint(await browserPush.currentEndpoint());
    } catch {
      // A service worker that is not ready yet is not an error worth showing: the next load
      // asks again, and the enable button works regardless.
      setSubscriptionEndpoint(null);
    }
  }, []);

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

  const loadPushSettings = useCallback(async () => {
    if (!activeFarm) return;
    try {
      const response = await notificationsApi.getPushSettings();
      setPushSettings(response.data);
    } catch {
      // A deployment without the push endpoints (or without the feature) answers 404, and a
      // settings page must still work: push is simply not on offer.
      setPushSettings({
        enabled: false,
        vapidPublicKey: null,
        maxSubscriptionsPerUser: 0,
        minSeverityOnByDefault: 'Critical',
        subscriptions: [],
      });
    }
  }, [activeFarm]);

  useEffect(() => {
  // oxlint-disable-next-line react/set-state-in-effect -- Initial fetch of preferences and push settings: server state an effect must synchronize.
    void load();
    void loadPushSettings();
    void readBrowserPush();
  }, [load, loadPushSettings, readBrowserPush]);

  const state: PushState = pushState({
    vapidPublicKey: pushSettings?.enabled ? pushSettings.vapidPublicKey ?? null : null,
    permission,
    endpoint: subscriptionEndpoint,
  });

  const handleEnablePush = async () => {
    if (!pushSettings?.vapidPublicKey) return;
    setPushBusy(true);
    try {
      const outcome = await enablePush({
        vapidPublicKey: pushSettings.vapidPublicKey,
        register: async (body) => (await notificationsApi.registerPushSubscription(body)).data,
      });

      if (outcome.status === 'subscribed') {
        message.success(t('pushTurnedOn'));
      } else if (outcome.status === 'denied') {
        message.warning(t('pushBlockedInBrowser'));
      } else if (outcome.status === 'unsupported') {
        message.warning(t('pushUnsupportedHere'));
      } else if (outcome.status === 'not-configured') {
        message.warning(t('pushTurnedOffOnServer'));
      } else {
        message.error(getApiError(outcome.error));
      }

      await Promise.all([loadPushSettings(), readBrowserPush()]);
    } finally {
      setPushBusy(false);
    }
  };

  const handleDisablePush = async () => {
    setPushBusy(true);
    try {
      const ids = (pushSettings?.subscriptions ?? [])
        .filter((s) => s.isActive)
        .map((s) => s.id);

      await disablePush({
        subscriptionIds: ids,
        unregister: (id) => notificationsApi.unregisterPushSubscription(id),
      });

      message.success(t('pushTurnedOff'));
      await Promise.all([loadPushSettings(), readBrowserPush()]);
    } finally {
      setPushBusy(false);
    }
  };

  const handleRemoveDevice = async (device: PushSubscriptionSummary) => {
    setPushBusy(true);
    try {
      await notificationsApi.unregisterPushSubscription(device.id);
      message.success(t('pushDeviceRemoved'));
      await loadPushSettings();
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setPushBusy(false);
    }
  };

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
    {
      title: t('push'),
      dataIndex: 'pushEnabled',
      key: 'pushEnabled',
      width: 120,
      render: (enabled: boolean, record) => (
        <Switch
          aria-label={t('ariaPushNotificationsFor', { alertType: alertTypeLabel(t, record.alertType) })}
          checked={enabled}
          // Disabled while there is no device to push to: an on switch with nothing behind it
          // reads as "this will buzz my phone", which is exactly what is not happening. The
          // stored value is untouched, so turning a device on later restores the choice.
          disabled={state !== 'subscribed'}
          onChange={(checked) => update(record.alertType, { pushEnabled: checked })}
        />
      ),
    },
  ];

  return (
    <div>
      {/* Layout (wrapping, and two rows on a phone) is `.fms-page-header` in AppLayout.css. */}
      <Space className="fms-page-header" style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
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

      <Card
        title={<Space><BellOutlined />{t('pushNotifications')}</Space>}
        style={{ marginBottom: 16 }}
        extra={<Text type="secondary">{pushStatusLabel(t, state)}</Text>}
      >
        <Space orientation="vertical" size={12} style={{ width: '100%' }}>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('pushGoesToTheDevicesYouTurnItOnFor')}
          </Paragraph>

          <Space wrap>
            {state === 'subscribed' ? (
              <Button danger loading={pushBusy} onClick={() => void handleDisablePush()}>
                {t('pushTurnOffThisDevice')}
              </Button>
            ) : (
              <Button
                type="primary"
                loading={pushBusy}
                // Off unless the browser and the server both allow it: the disabled button next
                // to the sentence above is the explanation, and the sentence is the reason.
                disabled={state !== 'available'}
                onClick={() => void handleEnablePush()}
              >
                {t('pushTurnOnThisDevice')}
              </Button>
            )}
            <Text type="secondary">
              {t('pushDevicesRegistered', {
                count: (pushSettings?.subscriptions ?? []).filter((s) => s.isActive).length,
              })}
            </Text>
          </Space>

          {(pushSettings?.subscriptions?.length ?? 0) > 0 && (
            <Table<PushSubscriptionSummary>
              rowKey="id"
              size="small"
              pagination={false}
              dataSource={pushSettings!.subscriptions}
              columns={[
                {
                  title: t('pushDevice'),
                  dataIndex: 'deviceLabel',
                  key: 'deviceLabel',
                  render: (label: string | null, device) => (
                    <Space size={8}>
                      <Text>{label || t('pushUnnamedDevice')}</Text>
                      {!device.isActive && <Tag>{t('pushNoLongerReceiving')}</Tag>}
                    </Space>
                  ),
                },
                {
                  title: t('pushLastSeen'),
                  dataIndex: 'lastSeenAtUtc',
                  key: 'lastSeenAtUtc',
                  render: (value: string) => new Date(value).toLocaleDateString(),
                },
                {
                  title: '',
                  key: 'remove',
                  width: 120,
                  render: (_: unknown, device) => (
                    <Button
                      size="small"
                      loading={pushBusy}
                      onClick={() => void handleRemoveDevice(device)}
                    >
                      {t('pushRemove')}
                    </Button>
                  ),
                },
              ]}
            />
          )}
        </Space>
      </Card>

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

/**
 * The one sentence that says what this device's push can do.
 *
 * Every state is named rather than implied: "push is off" and "push cannot work here" look
 * identical on a switch, and they are not the same problem to solve.
 */
function pushStatusLabel(t: (key: string) => string, state: PushState): string {
  switch (state) {
    case 'unsupported':
      return t('pushUnsupportedHere');
    case 'server-not-configured':
      return t('pushTurnedOffOnServer');
    case 'denied':
      return t('pushBlockedInBrowser');
    case 'subscribed':
      return t('pushOnThisDevice');
    default:
      return t('pushOffThisDevice');
  }
}

export default NotificationPreferencesPage;
