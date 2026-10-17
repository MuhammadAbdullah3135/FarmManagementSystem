import React from 'react';
import { Alert, Button, Space } from 'antd';
import { WarningOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { resetDemo } from '../api/demo';
import { useTranslation } from 'react-i18next';

/**
 * Persistent banner shown for the shared demo account.
 *
 * Two jobs:
 *  1. Make it obvious to the visitor that this is a shared demo session, not their own data,
 *     and point them at real sign-up.
 *  2. Give a demo visitor a way to restore the seeded sample data when they have made the demo
 *     messy (the demo is shared + resettable, see the Backend implementation).
 */
const DemoBanner: React.FC = () => {
  const { t } = useTranslation('common');
  const { user, isAuthenticated } = useAuthStore();
  const isDemo = Boolean(isAuthenticated && user?.isDemo);

  if (!isDemo) return null;

  return (
    <Alert
      type="warning"
      icon={<WarningOutlined />}
      showIcon
      style={{ marginBottom: 16 }}
      title={
        <Space orientation="vertical" size={4} style={{ width: '100%' }}>
          <span>{t('demo:bannerMessage')}</span>
          <Space size={8}>
            <Link to="/register">{t('demo:signUpLink')}</Link>
            <Button
              type="link"
              size="small"
              onClick={async () => {
                try {
                  await resetDemo();
                } catch {
                  /* The API already surfaces errors to the page via the existing error path;
                     a reset failure is not something to swallow silently, but it is also not
                     worth a second toast on top of whatever the next request produces. */
                }
              }}
            >
              {t('demo:resetDemoButton')}
            </Button>
          </Space>
        </Space>
      }
    />
  );
};

export default DemoBanner;
