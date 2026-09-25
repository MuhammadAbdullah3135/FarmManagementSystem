import React from 'react';
import { Form, Input, Button, Typography, Alert, Card, Result } from 'antd';
import { MailOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useTranslation } from 'react-i18next';

const { Title } = Typography;

const ResetPasswordPage: React.FC = () => {const { t } = useTranslation('auth'); 
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
            title={t('resetEmailSent')}
            subTitle={t('ifAnAccountExistsWithThisEmailYou')}
            extra={<Link to="/login">{t('backToLogin')}</Link>}
          />
        </Card>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center' }}>{t('resetPassword')}</Title>

        {error && (
          <Alert message={typeof error === 'string' ? error : t('passwordResetFailedUnexpectedResponse')} type="error" showIcon closable onClose={clearError} style={{ marginBottom: 24 }} />
        )}

        <Form form={form} onFinish={onFinish} layout="vertical">
          <Form.Item name="email" rules={[{ required: true, type: 'email', message: 'Please enter a valid email' }]}>
            <Input prefix={<MailOutlined />} placeholder={t('email')} size="large" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
              {t('sendResetLink')}
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

export default ResetPasswordPage;
