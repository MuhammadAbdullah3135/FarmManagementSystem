import React from 'react';
import { Form, Input, Button, Typography, Alert, Card, Result } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useTranslation } from 'react-i18next';

const { Title } = Typography;

const ConfirmResetPasswordPage: React.FC = () => {const { t } = useTranslation('auth'); 
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const [form] = Form.useForm();
  const { confirmResetPassword, isLoading, error, clearError } = useAuthStore();
  const [success, setSuccess] = React.useState(false);

  if (!token) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
        <Card style={{ width: 400 }}>
          <Result
            status="error"
            title={t('invalidResetLink')}
            subTitle={t('thisPasswordResetLinkIsInvalidOrMissing')}
            extra={<Link to="/reset-password">{t('requestNewResetLink')}</Link>}
          />
        </Card>
      </div>
    );
  }

  const onFinish = async (values: { newPassword: string; confirmPassword: string }) => {
    const result = await confirmResetPassword({ token, newPassword: values.newPassword });
    if (result) {
      setSuccess(true);
    }
  };

  if (success) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
        <Card style={{ width: 400 }}>
          <Result
            status="success"
            title={t('passwordResetSuccessful')}
            subTitle={t('yourPasswordHasBeenUpdatedYouCanNow')}
            extra={<Link to="/login">{t('goToLogin')}</Link>}
          />
        </Card>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center' }}>{t('setNewPassword')}</Title>

        {error && (
          <Alert message={typeof error === 'string' ? error : t('passwordResetFailedUnexpectedResponse')} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="newPassword" rules={[
            { required: true, message: 'Please enter a new password' },
            { min: 8, message: 'Password must be at least 8 characters' }
          ]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('newPassword')} size="large" />
          </Form.Item>

          <Form.Item
            name="confirmPassword"
            dependencies={['newPassword']}
            rules={[
              { required: true, message: 'Please confirm your new password' },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('newPassword') === value) {
                    return Promise.resolve();
                  }
                  return Promise.reject(new Error('Passwords do not match'));
                },
              }),
            ]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder={t('confirmNewPassword')} size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              {t('resetPassword')}
            </Button>
          </Form.Item>
        </Form>

        <div style={{ textAlign: 'center' }}>
          <Link to="/login">{t('backToLogin')}</Link>
        </div>
      </Card>
    </div>
  );
};

export default ConfirmResetPasswordPage;
