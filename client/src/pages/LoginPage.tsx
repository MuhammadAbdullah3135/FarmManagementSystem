import React, { useState } from 'react';
import { Form, Input, Button, Typography, Alert, Card } from 'antd';
import { MailOutlined, LockOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { BUILD_LABEL } from '../components/AppLayout';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

const LoginPage: React.FC = () => {
  const { t } = useTranslation(['auth', 'demo', 'validation']);
  const [form] = Form.useForm();
  const navigate = useNavigate();
  const { login, loginAsDemo, isLoading, error, clearError } = useAuthStore();
  const [demoLoading, setDemoLoading] = useState(false);

  const onFinish = async (values: { email: string; password: string }) => {
    const success = await login(values);
    if (success) {
      navigate('/dashboard');
    }
  };

  const handleTryDemo = async () => {
    setDemoLoading(true);
    try {
      const ok = await loginAsDemo();
      if (ok) {
        navigate('/dashboard');
      }
    } finally {
      setDemoLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center' }}>{t('farmManagementSystem')}</Title>
        <Title level={4} style={{ textAlign: 'center', marginTop: 0 }}>{t('signIn2')}</Title>

        {error && (
          <Alert message={typeof error === 'string' ? error : t('loginFailedUnexpectedResponse')} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="email" rules={[{ required: true, type: 'email', message: t('validation:common.emailInvalid') }]}>
            <Input prefix={<MailOutlined />} placeholder={t('email')} size="large" />
          </Form.Item>

          <Form.Item name="password" rules={[{ required: true, message: t('validation:common.passwordRequired') }]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('password')} size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              {t('signIn2')}
            </Button>
          </Form.Item>
        </Form>

        <div style={{ textAlign: 'center', marginTop: 8 }}>
          <Text type="secondary" style={{ fontSize: 13 }}>{t('demo:loginFormHint')}</Text>
          <br />
          <Button
            type="default"
            icon={<ThunderboltOutlined />}
            loading={demoLoading}
            onClick={handleTryDemo}
            size="large"
            style={{ width: '100%', marginTop: 6 }}
          >
            {t('demo:tryDemoButton')}
          </Button>
        </div>

        <div style={{ textAlign: 'center' }}>
          <Link to="/register">{t('createAnAccount')}</Link>
          <br />
          <Link to="/reset-password">{t('forgotPassword')}</Link>
        </div>

        {/* Visible before signing in, so a stale deployed bundle is obvious on the APK's first screen. */}
        <div style={{ textAlign: 'center', marginTop: 12 }}>
          <Text type="secondary" style={{ fontSize: 12 }}>{BUILD_LABEL}</Text>
        </div>
      </Card>
    </div>
  );
};

export default LoginPage;
