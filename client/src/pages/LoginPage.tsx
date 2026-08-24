import React from 'react';
import { Form, Input, Button, Typography, Alert, Card } from 'antd';
import { MailOutlined, LockOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';

const { Title } = Typography;

const LoginPage: React.FC = () => {
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
        <Title level={2} style={{ textAlign: 'center' }}>Farm Management System</Title>
        <Title level={4} style={{ textAlign: 'center', marginTop: 0 }}>Sign In</Title>

        {error && (
          <Alert message={error} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="email" rules={[{ required: true, type: 'email', message: 'Please enter a valid email' }]}>
            <Input prefix={<MailOutlined />} placeholder="Email" size="large" />
          </Form.Item>

          <Form.Item name="password" rules={[{ required: true, message: 'Please enter your password' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder="Password" size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              Sign In
            </Button>
          </Form.Item>
        </Form>

        <div style={{ textAlign: 'center' }}>
          <Link to="/register">Create an account</Link>
          <br />
          <Link to="/reset-password">Forgot password?</Link>
        </div>
      </Card>
    </div>
  );
};

export default LoginPage;
