import React from 'react';
import { Card, Typography, Row, Col, Statistic } from 'antd';
import { FormOutlined, TeamOutlined } from '@ant-design/icons';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';

const { Title, Text } = Typography;

const DashboardPage: React.FC = () => {
  const { user } = useAuthStore();
  const { farms, activeFarm } = useFarmStore();

  return (
    <div>
      <Title level={3}>Welcome, {user?.firstName}!</Title>
      <Text type="secondary">
        {activeFarm ? `Currently managing: ${activeFarm.name}` : 'Select a farm to get started'}
      </Text>

      <Row gutter={16} style={{ marginTop: 24 }}>
        <Col span={8}>
          <Card>
            <Statistic
              title="Total Farms"
              value={farms.length}
              prefix={<FormOutlined />}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card>
            <Statistic
              title="Active Farm"
              value={activeFarm?.name || 'None'}
              prefix={<TeamOutlined />}
            />
          </Card>
        </Col>
      </Row>

      {!activeFarm && farms.length > 0 && (
        <Card style={{ marginTop: 24 }}>
          <Text>Select a farm from the dropdown in the header to start managing your operations.</Text>
        </Card>
      )}

      {farms.length === 0 && (
        <Card style={{ marginTop: 24 }}>
          <Text>You don't have any farms yet. Create your first farm to get started!</Text>
        </Card>
      )}
    </div>
  );
};

export default DashboardPage;
