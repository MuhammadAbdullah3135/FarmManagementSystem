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
  CoffeeOutlined,
  BarChartOutlined,
  HeartOutlined,
  BugOutlined,
  NodeIndexOutlined,
  InboxOutlined,
  SettingOutlined,
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
        { key: '/dashboard/feed/types', icon: <CoffeeOutlined />, label: 'Feed Types' },
        { key: '/dashboard/feed/records', icon: <UnorderedListOutlined />, label: 'Feed Records' },
        { key: '/dashboard/feed/diet-plans', label: 'Diet Plans' },
        { key: '/dashboard/feed/schedules', label: 'Feeding Schedules' },
        { key: '/dashboard/feed/tasks', label: 'Feeding Tasks' },
        { key: '/dashboard/feed/reports', label: 'Reports' },
      ],
    },
    {
      key: '/dashboard/tasks',
      icon: <CheckSquareOutlined />,
      label: 'Farm Tasks',
    },
    {
      key: 'animals',
      icon: <BugOutlined />,
      label: 'Animals',
      children: [
        { key: '/dashboard/animals', icon: <UnorderedListOutlined />, label: 'Animal List' },
      ],
    },
    {
      key: 'breeding',
      icon: <NodeIndexOutlined />,
      label: 'Breeding',
      children: [
        { key: '/dashboard/breeding/records', icon: <UnorderedListOutlined />, label: 'Breeding Records' },
        { key: '/dashboard/breeding/gestation', icon: <HeartOutlined />, label: 'Gestation Tracking' },
        { key: '/dashboard/breeding/births', icon: <BugOutlined />, label: 'Birth Records' },
        { key: '/dashboard/breeding/lineage', icon: <NodeIndexOutlined />, label: 'Lineage View' },
        { key: '/dashboard/breeding/reports', icon: <BarChartOutlined />, label: 'Reports & Analytics' },
      ],
    },
    {
      key: 'hr',
      icon: <TeamOutlined />,
      label: 'HR',
      children: [
        { key: '/dashboard/hr/employees', label: 'Employees' },
        { key: '/dashboard/hr/departments-roles', label: 'Departments & Roles' },
        { key: '/dashboard/hr/salary-payments', icon: <DollarOutlined />, label: 'Salary & Payroll' },
        { key: '/dashboard/hr/attendance', icon: <ClockCircleOutlined />, label: 'Attendance' },
        { key: '/dashboard/hr/performance', icon: <StarOutlined />, label: 'Performance Reviews' },
      ],
    },
    {
      key: 'finance',
      icon: <DollarOutlined />,
      label: 'Finance',
      children: [
        { key: '/dashboard/finance/expenses', icon: <AuditOutlined />, label: 'Expenses' },
        { key: '/dashboard/finance/incomes', icon: <AuditOutlined />, label: 'Income' },
        { key: '/dashboard/finance/reports', icon: <BarChartOutlined />, label: 'Reports' },
        { key: '/dashboard/finance/categories', icon: <UnorderedListOutlined />, label: 'Categories & Payment Methods' },
      ],
    },
    {
      key: 'inventory',
      icon: <InboxOutlined />,
      label: 'Inventory',
      children: [
        { key: '/dashboard/inventory/items', icon: <UnorderedListOutlined />, label: 'Inventory Items' },
        { key: '/dashboard/inventory/movements', icon: <SwapOutlined />, label: 'Stock Movements' },
        { key: '/dashboard/inventory/suppliers', icon: <TeamOutlined />, label: 'Suppliers' },
        { key: '/dashboard/inventory/customers', icon: <TeamOutlined />, label: 'Customers' },
        { key: '/dashboard/inventory/reports', icon: <BarChartOutlined />, label: 'Reports' },
      ],
    },
    {
      key: 'health',
      icon: <HeartOutlined />,
      label: 'Health Management',
      children: [
        { key: '/dashboard/health/medical', icon: <MedicineBoxOutlined />, label: 'Medical Records' },
        { key: '/dashboard/health/medicines', icon: <MedicineBoxOutlined />, label: 'Medicines' },
        { key: '/dashboard/health/medicines/alerts', icon: <MedicineBoxOutlined />, label: 'Medicine Alerts' },
        { key: '/dashboard/health/vaccines', label: 'Vaccine Types' },
        { key: '/dashboard/health/vaccinations', label: 'Vaccination Records' },
        { key: '/dashboard/health/vaccinations/schedule', label: 'Vaccination Schedules' },
        { key: '/dashboard/health/weight-schedules', label: 'Weight Check Schedules' },
        { key: '/dashboard/health/costs', label: 'Vet Costs' },
      ],
    },
    {
      key: 'reports',
      icon: <BarChartOutlined />,
      label: 'Reports',
      children: [
        { key: '/dashboard/reports/animals', label: 'Animal Reports' },
        { key: '/dashboard/reports/financial', label: 'Financial Reports' },
        { key: '/dashboard/reports/feed', label: 'Feed Reports' },
        { key: '/dashboard/reports/medical', label: 'Medical Reports' },
        { key: '/dashboard/reports/vaccination', label: 'Vaccination Reports' },
        { key: '/dashboard/reports/breeding', label: 'Breeding Reports' },
        { key: '/dashboard/reports/employees', label: 'Employee Reports' },
      ],
    },
    {
      key: 'admin',
      icon: <SettingOutlined />,
      label: 'Admin',
      children: [
        { key: '/dashboard/admin/audit-log', icon: <AuditOutlined />, label: 'Audit Log' },
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
