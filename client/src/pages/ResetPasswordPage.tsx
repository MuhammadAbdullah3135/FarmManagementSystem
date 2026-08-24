import React from 'react';
import { Form, Input, Button, Typography, Alert, Card, Result } from 'antd';
import { MailOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';

const { Title } = Typography;

const ResetPasswordPage: React.FC = () => {
  const [form] = Form.useForm();
  const { resetPassword, isLoading, error, clearError } = useAuthStore();
  const [success, setSuccess] = React.useState(false);

  const onFinish = async (values: { email: string }) => {
    const result = await resetPassword(values);
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
            title="Reset Email Sent"
            subTitle="If an account exists with this email, you will receive a password reset link."
            extra={<Link to="/login">Back to Login</Link>}
          />
        </Card>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center' }}>Reset Password</Title>

        {error && (
          <Alert message={error} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="email" rules={[{ required: true, type: 'email', message: 'Please enter a valid email' }]}>
            <Input prefix={<MailOutlined />} placeholder="Email" size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              Send Reset Link
            </Button>
          </Form.Item>
        </Form>

        <div style={{ textAlign: 'center' }}>
          <Link to="/login">Back to Login</Link>
        </div>
      </Card>
    </div>
  );
};

export default ResetPasswordPage;
