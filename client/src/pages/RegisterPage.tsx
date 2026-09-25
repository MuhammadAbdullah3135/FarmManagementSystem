import React from 'react';
import { Form, Input, Button, Typography, Alert, Card } from 'antd';
import { MailOutlined, LockOutlined, UserOutlined, BankOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useTranslation } from 'react-i18next';

const { Title } = Typography;

const RegisterPage: React.FC = () => {const { t } = useTranslation('auth'); 
  const [form] = Form.useForm();
  const navigate = useNavigate();
  const { register, isLoading, error, clearError } = useAuthStore();

  const onFinish = async (values: {
    email: string;
    password: string;
    firstName: string;
    lastName: string;
    accountName: string;
  }) => {
    const success = await register(values);
    if (success) {
      navigate('/dashboard');
    }
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 450 }}>
        <Title level={2} style={{ textAlign: 'center' }}>{t('farmManagementSystem')}</Title>
        <Title level={4} style={{ textAlign: 'center', marginTop: 0 }}>{t('createAccount2')}</Title>

        {error && (
          <Alert message={typeof error === 'string' ? error : t('registrationFailedUnexpectedResponse')} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="accountName" rules={[{ required: true, message: 'Please enter account name' }]}>
            <Input prefix={<BankOutlined />} placeholder={t('accountOrganizationName')} size="large" />
          </Form.Item>

          <div style={{ display: 'flex', gap: 16 }}>
            <Form.Item name="firstName" rules={[{ required: true, message: 'Please enter first name' }]} style={{ flex: 1 }}>
              <Input prefix={<UserOutlined />} placeholder={t('firstName')} size="large" />
            </Form.Item>

            <Form.Item name="lastName" rules={[{ required: true, message: 'Please enter last name' }]} style={{ flex: 1 }}>
              <Input placeholder={t('lastName')} size="large" />
            </Form.Item>
          </div>

          <Form.Item name="email" rules={[{ required: true, type: 'email', message: 'Please enter a valid email' }]}>
            <Input prefix={<MailOutlined />} placeholder={t('email')} size="large" />
          </Form.Item>

          <Form.Item name="password" rules={[{ required: true, min: 8, message: 'Password must be at least 8 characters' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('password')} size="large" />
          </Form.Item>

          <Form.Item
            name="confirmPassword"
            dependencies={['password']}
            rules={[
              { required: true, message: 'Please confirm password' },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('password') === value) {
                    return Promise.resolve();
                  }
                  return Promise.reject(new Error('Passwords do not match'));
                },
              }),
            ]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder={t('confirmPassword')} size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              {t('createAccount2')}
            </Button>
          </Form.Item>
        </Form>

        <div style={{ textAlign: 'center' }}>
          {t('alreadyHaveAnAccount')} <Link to="/login">{t('signIn')}</Link>
        </div>
      </Card>
    </div>
  );
};

export default RegisterPage;
