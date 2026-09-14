import React, { useEffect, useState } from 'react';
import '../AppLayout.css';
import { Layout, Menu, Typography, Dropdown, Avatar, Button, Drawer } from 'antd';
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
  MenuOutlined,
  CloseOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';

const { Header, Sider, Content } = Layout;
const { Text } = Typography;

const base = import.meta.env.BASE_URL.replace(/\/+$/, '');
const stripBase = (pathname: string) =>
  base && pathname.startsWith(base) ? pathname.slice(base.length) || '/' : pathname;

const AppLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, logout } = useAuthStore();
  const { farms, activeFarm, fetchFarms, setActiveFarm } = useFarmStore();

  const [isMobile, setIsMobile] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('screen and (max-width: 991.98px)').matches,
  );
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  useEffect(() => {
    fetchFarms();
  }, [fetchFarms]);

  useEffect(() => {
    const root = document.getElementById('root');
    document.body.style.height = '100vh';
    document.body.style.overflow = 'hidden';
    if (root) {
      root.style.height = '100vh';
      root.style.minHeight = '100vh';
      root.style.overflow = 'hidden';
    }
    return () => {
      document.body.style.height = '';
      document.body.style.overflow = '';
      if (root) {
        root.style.height = '';
        root.style.minHeight = '';
        root.style.overflow = '';
      }
    };
  }, []);

  useEffect(() => {
    if (!isMobile) {
      setMobileMenuOpen(false);
    }
  }, [isMobile]);

  useEffect(() => {
    setMobileMenuOpen(false);
  }, [location.pathname]);

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

  const renderNav = (onClose?: () => void) => (
    <>
      <div style={{ padding: '16px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <Text strong style={{ color: '#fff', fontSize: 18 }}>FMS</Text>
        {onClose && (
          <Button
            type="text"
            size="small"
            icon={<CloseOutlined />}
            onClick={onClose}
            aria-label="Close menu"
            style={{ color: '#fff', marginLeft: 'auto' }}
          />
        )}
      </div>
      <div className="fms-sider-scroll" style={{ flex: 1, minHeight: 0, overflowY: 'auto', overflowX: 'hidden' }}>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[stripBase(location.pathname)]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
      </div>
    </>
  );

  return (
    <Layout style={{ height: '100vh', overflow: 'hidden' }}>
      <Sider
        theme="dark"
        breakpoint="lg"
        collapsedWidth="0"
        trigger={null}
        onBreakpoint={setIsMobile}
        style={{ height: '100vh', overflow: 'hidden', display: isMobile ? 'none' : undefined }}
        styles={{
          body: { height: '100%', display: 'flex', flexDirection: 'column' },
        }}
      >
        {renderNav()}
      </Sider>

      <Layout style={{ overflow: 'hidden' }}>
        <Header style={{ background: '#fff', padding: isMobile ? '0 12px' : '0 24px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexShrink: 0, gap: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
            {isMobile && (
              <Button
                type="text"
                icon={<MenuOutlined />}
                onClick={() => setMobileMenuOpen(true)}
                aria-label="Open menu"
              />
            )}
            <Dropdown menu={{ items: farmMenuItems }} trigger={['click']}>
              <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                <SwapOutlined />
                {activeFarm ? (
                  <Text strong style={{ textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap', maxWidth: isMobile ? 120 : undefined }}>{activeFarm.name}</Text>
                ) : farms.length > 0 ? (
                  <Text type="secondary">Select a farm</Text>
                ) : (
                  <Text type="secondary">No farms yet</Text>
                )}
              </div>
            </Dropdown>
          </div>

          <Dropdown menu={{ items: userMenuItems }} trigger={['click']}>
            <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
              <Avatar icon={<UserOutlined />} />
              <Text style={{ textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap', maxWidth: isMobile ? 90 : undefined }}>
                {user?.firstName} {user?.lastName}
              </Text>
            </div>
          </Dropdown>
        </Header>

        <Content style={{ flex: 1, overflowY: 'auto', margin: isMobile ? 8 : 24, padding: isMobile ? 8 : 24, background: '#fff', minHeight: 280 }}>
          <Outlet />
        </Content>
      </Layout>

      <Drawer
        placement="left"
        open={mobileMenuOpen}
        onClose={() => setMobileMenuOpen(false)}
        width={240}
        closable={false}
        styles={{
          header: { display: 'none' },
          body: { backgroundColor: '#001529', padding: 0 },
        }}
      >
        <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
          {renderNav(() => setMobileMenuOpen(false))}
        </div>
      </Drawer>
    </Layout>
  );
};

export default AppLayout;
