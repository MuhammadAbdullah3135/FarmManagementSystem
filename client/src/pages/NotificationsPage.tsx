import React, { useCallback, useEffect, useState } from 'react';
import {
  Badge, Button, Card, Empty, Pagination, Space, Spin, Switch, Tag, Typography, message,
} from 'antd';
import { BellOutlined, CheckOutlined, DeleteOutlined, SettingOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import {
  notificationsApi, alertTypeLabel, severityColor,
} from '../api/notifications';
import type { Notification, NotificationListParams } from '../api/notifications';
import { getApiError } from '../api/farmApi';
import { useFarmStore } from '../stores/farmStore';

const { Title, Text, Paragraph } = Typography;

const PAGE_SIZE = 20;

/**
 * The notification center.
 *
 * The dashboard's alert cards are a snapshot of the current conditions; this is
 * the durable list, with read and dismissed state that persists. Alerts are
 * written by the scheduled `notification-dispatch` job, so this page never
 * computes anything — it reads what was recorded.
 *
 * "View" reuses the same relative route the dashboard cards link to, so an alert
 * navigates identically from either place.
 */
const NotificationsPage: React.FC = () => {
  const navigate = useNavigate();
  const { activeFarm } = useFarmStore();

  const [items, setItems] = useState<Notification[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [includeDismissed, setIncludeDismissed] = useState(false);
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    if (!activeFarm) return;
    setLoading(true);
    try {
      const params: NotificationListParams = {
        unreadOnly,
        includeDismissed,
        page,
        pageSize: PAGE_SIZE,
      };
      const response = await notificationsApi.list(params);
      setItems(response.data.items);
      setTotalCount(response.data.totalCount);
      setUnreadCount(response.data.unreadCount);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [activeFarm, unreadOnly, includeDismissed, page]);

  useEffect(() => {
    void load();
  }, [load]);

  // Changing a filter can leave the current page past the end of the result, so
  // the reset happens with the change (in the handler) rather than in an effect
  // that would render the new filter against a stale page first.
  const changeFilter = (apply: () => void) => {
    setPage(1);
    apply();
  };

  const handleMarkRead = async (notification: Notification) => {
    try {
      await notificationsApi.markRead(notification.id);
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleDismiss = async (notification: Notification) => {
    try {
      await notificationsApi.dismiss(notification.id);
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleMarkAllRead = async () => {
    try {
      const response = await notificationsApi.markAllRead();
      message.success(`${response.data} notification(s) marked read.`);
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
        <div>
          <Title level={3} style={{ marginBottom: 0 }}>
            Notifications <Badge count={unreadCount} overflowCount={99} />
          </Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Your alerts for {activeFarm?.name ?? 'this farm'}, recorded by the scheduled dispatch job.
            Marking one read or dismissing it only affects your own list.
          </Paragraph>
        </div>
        <Space>
          <Switch
            aria-label="Unread only"
            checked={unreadOnly}
            onChange={(checked) => changeFilter(() => setUnreadOnly(checked))}
            checkedChildren="Unread"
            unCheckedChildren="All"
          />
          <Switch
            aria-label="Show dismissed"
            checked={includeDismissed}
            onChange={(checked) => changeFilter(() => setIncludeDismissed(checked))}
            checkedChildren="Dismissed shown"
            unCheckedChildren="Dismissed hidden"
          />
          <Button
            icon={<SettingOutlined />}
            onClick={() => navigate('/dashboard/notifications/preferences')}
          >
            Preferences
          </Button>
          <Button icon={<CheckOutlined />} onClick={() => void handleMarkAllRead()} disabled={unreadCount === 0}>
            Mark all read
          </Button>
        </Space>
      </Space>

      <Card>
        <Spin spinning={loading}>
          {items.length === 0 ? (
            <Empty
              image={<BellOutlined style={{ fontSize: 32 }} />}
              description={
                includeDismissed ? 'No notifications.' : 'Nothing needs attention right now.'
              }
            />
          ) : (
            <Space orientation="vertical" size={12} style={{ width: '100%' }}>
              {items.map((notification) => (
                <div
                  key={notification.id}
                  style={{
                    border: '1px solid #f0f0f0',
                    borderRadius: 6,
                    padding: '12px 16px',
                    display: 'flex',
                    gap: 16,
                    alignItems: 'flex-start',
                  }}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <Space size={8} wrap>
                      <Tag color={severityColor(notification.severity)}>{notification.severity}</Tag>
                      <Tag>{alertTypeLabel(notification.alertType)}</Tag>
                      <Text strong={!notification.isRead}>{notification.title}</Text>
                      {notification.isRead ? null : <Badge status="processing" text="Unread" />}
                      {notification.isDismissed ? <Tag>Dismissed</Tag> : null}
                    </Space>
                    <div style={{ marginTop: 4 }}>{notification.message}</div>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {notification.dueDate
                        ? `Due ${dayjs(notification.dueDate).format('MMM D, YYYY')} · `
                        : ''}
                      recorded {dayjs(notification.createdAt).format('MMM D, YYYY HH:mm')}
                      {notification.deliveredAtUtc ? ' · emailed' : ''}
                    </Text>
                  </div>

                  <Space size={4} wrap>
                    {notification.link ? <Link to={notification.link}>View</Link> : null}
                    {notification.isRead ? null : (
                      <Button
                        type="link"
                        size="small"
                        onClick={() => void handleMarkRead(notification)}
                      >
                        Mark read
                      </Button>
                    )}
                    {notification.isDismissed ? null : (
                      <Button
                        type="link"
                        size="small"
                        icon={<DeleteOutlined />}
                        onClick={() => void handleDismiss(notification)}
                      >
                        Dismiss
                      </Button>
                    )}
                  </Space>
                </div>
              ))}
            </Space>
          )}
        </Spin>

        {totalCount > PAGE_SIZE && (
          <Pagination
            style={{ marginTop: 16, textAlign: 'right' }}
            current={page}
            pageSize={PAGE_SIZE}
            total={totalCount}
            showSizeChanger={false}
            onChange={setPage}
          />
        )}
      </Card>
    </div>
  );
};

export default NotificationsPage;
