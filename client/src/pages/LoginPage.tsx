import React from 'react';
import { Form, Input, Button, Typography, Alert, Card } from 'antd';
import { MailOutlined, LockOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { BUILD_LABEL } from '../components/AppLayout';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

const LoginPage: React.FC = () => {const { t } = useTranslation('auth'); 
  const [form] = Form.useForm();
  const navigate = useNavigate();
  const { login, isLoading, error, clearError } = useAuthStore();

  const onFinish = async (values: { email: string; password: string }) => {
    const success = await login(values);
    if (success) {
      navigate('/dashboard');
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
          <Form.Item name="email" rules={[{ required: true, type: 'email', message: 'Please enter a valid email' }]}>
            <Input prefix={<MailOutlined />} placeholder={t('email')} size="large" />
          </Form.Item>

          <Form.Item name="password" rules={[{ required: true, message: 'Please enter your password' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('password')} size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              {t('signIn2')}
            </Button>
          </Form.Item>
        </Form>

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
