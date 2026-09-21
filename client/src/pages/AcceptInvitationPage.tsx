import React, { useCallback, useEffect, useState } from 'react';
import { Button, Card, List, Result, Space, Tag, Typography, message } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import dayjs from 'dayjs';
import { invitationsApi, type PendingInvitation } from '../api/members';
import { getApiError } from '../api/farmApi';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';

const { Title, Text } = Typography;

const AcceptInvitationPage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const navigate = useNavigate();
  const { isAuthenticated } = useAuthStore();
  const fetchFarms = useFarmStore((s) => s.fetchFarms);

  const [pending, setPending] = useState<PendingInvitation[]>([]);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<'accepted' | 'declined' | null>(null);

  const loadPending = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const res = await invitationsApi.pendingForMe();
      setPending(res.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, [isAuthenticated]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadPending(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadPending]);

  const handleAccept = async () => {
    if (!token) return;
    setBusy(true);
    try {
      await invitationsApi.accept(token);
      // Refresh the farm list so the newly joined farm appears immediately.
      await fetchFarms();
      setDone('accepted');
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setBusy(false);
    }
  };

  const handleDecline = async () => {
    if (!token) return;
    setBusy(true);
    try {
      await invitationsApi.decline(token);
      setDone('declined');
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setBusy(false);
    }
  };

  const card = (children: React.ReactNode) => (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5', padding: 16 }}>
      <Card style={{ width: 480, maxWidth: '100%' }}>{children}</Card>
    </div>
  );

  if (!isAuthenticated) {
    return card(
      <Result
        status="info"
        title="Sign in to accept your invitation"
        subTitle="Invitations are tied to your email address, so sign in (or create an account with the invited email) and then open the invitation link again."
        extra={
          <Space>
            <Link to="/login"><Button type="primary">Sign in</Button></Link>
            <Link to="/register"><Button>Create account</Button></Link>
          </Space>
        }
      />,
    );
  }

  if (done === 'accepted') {
    return card(
      <Result
        status="success"
        title="Invitation accepted"
        subTitle="The farm has been added to your farm list. Switch to it from the farm selector in the header."
        extra={<Button type="primary" onClick={() => navigate('/dashboard')}>Go to dashboard</Button>}
      />,
    );
  }

  if (done === 'declined') {
    return card(
      <Result
        status="info"
        title="Invitation declined"
        subTitle="No changes were made to your farm memberships."
        extra={<Button onClick={() => navigate('/dashboard')}>Go to dashboard</Button>}
      />,
    );
  }

  return card(
    <>
      <Title level={3} style={{ marginTop: 0 }}>Farm invitation</Title>

      {token ? (
        <>
          <Text>You have been invited to join a farm. Accepting adds it to your farm list.</Text>
          <div style={{ marginTop: 20 }}>
            <Space>
              <Button type="primary" loading={busy} onClick={() => void handleAccept()}>
                Accept invitation
              </Button>
              <Button disabled={busy} onClick={() => void handleDecline()}>
                Decline
              </Button>
            </Space>
          </div>
        </>
      ) : (
        <Text type="secondary">
          This page needs the invitation token from the link in your invitation email.
        </Text>
      )}

      {pending.length > 0 && (
        <div style={{ marginTop: 28 }}>
          <Text strong>Invitations waiting for you</Text>
          <List
            size="small"
            style={{ marginTop: 8 }}
            dataSource={pending}
            renderItem={(invite) => (
              <List.Item>
                <Space direction="vertical" size={0}>
                  <Text>{invite.farmName}</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    <Tag>{invite.role}</Tag>
                    Expires {dayjs(invite.expiresAt).format('YYYY-MM-DD')}
                    {invite.invitedByName ? ` · invited by ${invite.invitedByName}` : ''}
                  </Text>
                </Space>
              </List.Item>
            )}
          />
        </div>
      )}
    </>,
  );
};

export default AcceptInvitationPage;
