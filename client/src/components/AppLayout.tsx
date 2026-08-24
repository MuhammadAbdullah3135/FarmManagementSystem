import React, { useEffect } from 'react';
import { Layout, Menu, Typography, Dropdown, Avatar } from 'antd';
import {
  DashboardOutlined,
  SwapOutlined,
  LogoutOutlined,
  UserOutlined,
  MedicineBoxOutlined,
  TeamOutlined,
  UnorderedListOutlined,
  AuditOutlined,
  DollarOutlined,
  ClockCircleOutlined,
  StarOutlined,
  CheckSquareOutlined,
  FeedOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';

const { Header, Sider, Content } = Layout;
const { Text } = Typography;

const AppLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, logout } = useAuthStore();
  const { farms, activeFarm, fetchFarms, setActiveFarm } = useFarmStore();

  useEffect(() => {
    fetchFarms();
  }, [fetchFarms]);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  const farmMenuItems = farms.map((farm) => ({
    key: farm.id,
    label: farm.name,
    onClick: () => setActiveFarm(farm),
  }));

  const userMenuItems = [
    {
      key: 'profile',
      label: user?.email,
      disabled: true,
    },
    {
      type: 'divider' as const,
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Logout',
      onClick: handleLogout,
    },
  ];

  const menuItems = [
    {
      key: '/dashboard',
      icon: <DashboardOutlined />,
      label: 'Dashboard',
    },
    {
      key: 'feed',
      icon: <MedicineBoxOutlined />,
      label: 'Feed Management',
      children: [
        { key: '/feed/types', icon: <FeedOutlined />, label: 'Feed Types' },
        { key: '/feed/records', icon: <UnorderedListOutlined />, label: 'Feed Records' },
        { key: '/feed/diet-plans', label: 'Diet Plans' },
        { key: '/feed/schedules', label: 'Feeding Schedules' },
        { key: '/feed/tasks', label: 'Feeding Tasks' },
        { key: '/feed/reports', label: 'Reports' },
      ],
    },
    {
      key: '/tasks',
      icon: <CheckSquareOutlined />,
      label: 'Farm Tasks',
    },
    {
      key: 'hr',
      icon: <TeamOutlined />,
      label: 'HR',
      children: [
        { key: '/hr/employees', label: 'Employees' },
        { key: '/hr/departments-roles', label: 'Departments & Roles' },
        { key: '/hr/salary-payments', icon: <DollarOutlined />, label: 'Salary & Payroll' },
        { key: '/hr/attendance', icon: <ClockCircleOutlined />, label: 'Attendance' },
        { key: '/hr/performance', icon: <StarOutlined />, label: 'Performance Reviews' },
      ],
    },
  ];

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider theme="dark" breakpoint="lg" collapsedWidth="0">
        <div style={{ padding: '16px', textAlign: 'center' }}>
          <Text strong style={{ color: '#fff', fontSize: 18 }}>FMS</Text>
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
      </Sider>

      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Dropdown menu={{ items: farmMenuItems }} trigger={['click']}>
            <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8 }}>
              <SwapOutlined />
              {activeFarm ? (
                <Text strong>{activeFarm.name}</Text>
              ) : farms.length > 0 ? (
                <Text type="secondary">Select a farm</Text>
              ) : (
                <Text type="secondary">No farms yet</Text>
              )}
            </div>
          </Dropdown>

          <Dropdown menu={{ items: userMenuItems }} trigger={['click']}>
            <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8 }}>
              <Avatar icon={<UserOutlined />} />
              <Text>{user?.firstName} {user?.lastName}</Text>
            </div>
          </Dropdown>
        </Header>

        <Content style={{ margin: '24px', padding: 24, background: '#fff', minHeight: 280 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
};

export default AppLayout;
