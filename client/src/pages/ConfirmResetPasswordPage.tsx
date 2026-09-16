import React from 'react';
import { Form, Input, Button, Typography, Alert, Card, Result } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';

const { Title } = Typography;

const ConfirmResetPasswordPage: React.FC = () => {
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
            title="Invalid Reset Link"
            subTitle="This password reset link is invalid or missing a token. Please request a new one."
            extra={<Link to="/reset-password">Request New Reset Link</Link>}
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
            title="Password Reset Successful"
            subTitle="Your password has been updated. You can now log in with your new password."
            extra={<Link to="/login">Go to Login</Link>}
          />
        </Card>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center' }}>Set New Password</Title>

        {error && (
          <Alert message={typeof error === 'string' ? error : 'Password reset failed (unexpected response)'} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="newPassword" rules={[
            { required: true, message: 'Please enter a new password' },
            { min: 8, message: 'Password must be at least 8 characters' }
          ]}>
            <Input.Password prefix={<LockOutlined />} placeholder="New Password" size="large" />
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
            <Input.Password prefix={<LockOutlined />} placeholder="Confirm New Password" size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              Reset Password
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

export default ConfirmResetPasswordPage;
