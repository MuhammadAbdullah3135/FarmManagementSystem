import React, { useEffect, useState } from 'react';
import '../AppLayout.css';
import { Layout, Menu, Typography, Dropdown, Avatar, Badge, Button, Drawer, Result, Space, Spin, message } from 'antd';
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
  BellOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { filterMenuByRole } from '../utils/permissions';
import type { AppMenuItem } from '../utils/permissions';
import { usePermissions } from '../hooks/usePermissions';
import { setFarmAccessDeniedHandler } from '../api/axios';
import { notificationsApi } from '../api/notifications';
import type { MenuProps } from 'antd';

const { Header, Sider, Content } = Layout;
const { Text } = Typography;

// Injected at build time by the `define` block in vite.config.ts (Pages passes VITE_BUILD_SHA).
// Declared here rather than in a new *.d.ts because the annotation is program-wide for the app
// tsconfig, so LoginPage can read the same identifier. The `typeof` guard keeps the reference
// safe anywhere the define is not applied (e.g. an unexpected transform).
declare global {
  const __BUILD_SHA__: string;
  const __BUILD_TIME__: string;
}

const BUILD_SHA = typeof __BUILD_SHA__ === 'undefined' ? 'dev' : __BUILD_SHA__;
const BUILD_TIME = typeof __BUILD_TIME__ === 'undefined' ? '' : __BUILD_TIME__;
/** Shown in the UI so a stale deployed bundle is obvious at a glance. */
export const BUILD_LABEL = `Build ${BUILD_SHA === 'dev' ? 'dev' : BUILD_SHA.slice(0, 7)}${BUILD_TIME ? ` · ${BUILD_TIME.slice(0, 10)}` : ''}`;

document.documentElement.dataset.build = BUILD_SHA;

const base = import.meta.env.BASE_URL.replace(/\/+$/, '');
const stripBase = (pathname: string) =>
  base && pathname.startsWith(base) ? pathname.slice(base.length) || '/' : pathname;

/**
 * The navigation tree. Entries whose whole module is role-restricted carry a
 * `requiredModule` key from `MODULE_ROLES`; `filterMenuByRole` hides them (and
 * any group left empty) for users whose roles do not grant access.
 */
export const appMenuItems: AppMenuItem[] = [
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
    // Ungated on purpose: every member can read their *own* notifications, and
    // the dispatcher already withholds alerts a role cannot act on, so there is
    // no module for a menu filter to check.
    key: '/dashboard/notifications',
    icon: <BellOutlined />,
    label: 'Notifications',
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
      { key: '/dashboard/inventory/items', icon: <UnorderedListOutlined />, label: 'Inventory Items', requiredModule: 'inventory.items' },
      { key: '/dashboard/inventory/movements', icon: <SwapOutlined />, label: 'Stock Movements', requiredModule: 'inventory.items' },
      { key: '/dashboard/inventory/suppliers', icon: <TeamOutlined />, label: 'Suppliers', requiredModule: 'inventory.suppliers' },
      { key: '/dashboard/inventory/customers', icon: <TeamOutlined />, label: 'Customers', requiredModule: 'inventory.customers' },
      { key: '/dashboard/inventory/reports', icon: <BarChartOutlined />, label: 'Reports', requiredModule: 'inventory.items' },
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
    key: 'configuration',
    icon: <SettingOutlined />,
    label: 'Configuration',
    children: [
      { key: '/dashboard/configuration', icon: <SettingOutlined />, label: 'Farm Configuration' },
      { key: '/dashboard/farm/members', icon: <TeamOutlined />, label: 'Members', requiredModule: 'farm.members' },
    ],
  },
  {
    key: 'admin',
    icon: <AuditOutlined />,
    label: 'Admin',
    children: [
      { key: '/dashboard/admin/audit-log', icon: <AuditOutlined />, label: 'Audit Log', requiredModule: 'admin.audit-log' },
      { key: '/dashboard/admin/jobs', icon: <ClockCircleOutlined />, label: 'Scheduled Jobs', requiredModule: 'admin.jobs' },
    ],
  },
];

/**
 * Routes that are account-scoped rather than farm-scoped.
 *
 * The content area normally waits for an active farm, because almost every
 * screen is farm data. The job-status view is infrastructure, not farm data
 * (its endpoint is /api/admin/jobs, with no farm in the path), so it must stay
 * reachable even when the user has no farm selected.
 */
const farmIndependentPaths = ['/dashboard/admin/jobs'];

const isFarmIndependent = (pathname: string) =>
  farmIndependentPaths.some(
    (path) => pathname === path || pathname.startsWith(`${path}/`),
  );

const AppLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, logout } = useAuthStore();
  const {
    farms, activeFarm, fetchFarms, setActiveFarm, clearActiveFarm,
    isLoading: isLoadingFarms,
  } = useFarmStore();
  const { roles } = usePermissions();

  const [isMobile, setIsMobile] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('screen and (max-width: 991.98px)').matches,
  );
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [unreadNotifications, setUnreadNotifications] = useState(0);

  useEffect(() => {
    fetchFarms();
  }, [fetchFarms]);

  // The badge is a pointer, not a feed: it is refreshed when the active farm
  // changes and on navigation (which covers returning from the notification
  // center after marking things read). Deliberately no polling or websocket —
  // a stale count is worth less than a request every few seconds on every page.
  useEffect(() => {
    let cancelled = false;

    const loadUnread = async () => {
      if (!activeFarm) {
        setUnreadNotifications(0);
        return;
      }

      try {
        const response = await notificationsApi.unreadCount();
        // A badge renders a count, never whatever the boundary happened to
        // return, so a non-numeric body degrades to "no badge" rather than to a
        // broken header.
        const count = typeof response.data === 'number' ? response.data : 0;
        if (!cancelled) setUnreadNotifications(count);
      } catch {
        // The axios interceptor already reports failures; a badge is not worth
        // interrupting the page with an error toast.
      }
    };

    void loadUnread();
    return () => {
      cancelled = true;
    };
  }, [activeFarm, location.pathname]);

  // React to losing membership of the active farm while the app is open: drop
  // the farm, refresh the list, and tell the user why their screens went quiet.
  // The API only signals this with its specific farm-context 403 body, so an
  // ordinary role-based 403 never lands here.
  useEffect(() => {
    setFarmAccessDeniedHandler(() => {
      clearActiveFarm();
      void fetchFarms();
      message.warning('Your access to that farm was removed.');
    });

    return () => setFarmAccessDeniedHandler(null);
  }, [clearActiveFarm, fetchFarms]);

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
      key: 'build',
      label: BUILD_LABEL,
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

  const menuItems = filterMenuByRole(appMenuItems, roles);

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
          items={menuItems as MenuProps['items']}
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

          <Space size={4} style={{ minWidth: 0 }}>
            <Button
              type="text"
              aria-label="Notifications"
              onClick={() => navigate('/dashboard/notifications')}
            >
              <Badge count={unreadNotifications} size="small" overflowCount={99}>
                <BellOutlined style={{ fontSize: 16 }} />
              </Badge>
            </Button>

            <Dropdown menu={{ items: userMenuItems }} trigger={['click']}>
            <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
              <Avatar icon={<UserOutlined />} />
              <Text style={{ textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap', maxWidth: isMobile ? 90 : undefined }}>
                {user?.firstName} {user?.lastName}
              </Text>
            </div>
            </Dropdown>
          </Space>
        </Header>

        <Content style={{ flex: 1, overflowY: 'auto', margin: isMobile ? 8 : 24, padding: isMobile ? 8 : 24, background: '#fff', minHeight: 280 }}>
          {activeFarm || isFarmIndependent(stripBase(location.pathname)) ? (
            <Outlet />
          ) : isLoadingFarms ? (
            <div style={{ textAlign: 'center', padding: 48 }}>
              <Spin />
            </div>
          ) : (
            <Result
              status="info"
              title={farms.length > 0 ? 'Select a farm to continue' : 'No farm access'}
              subTitle={
                farms.length > 0
                  ? 'Choose a farm from the selector in the header.'
                  : 'You are not a member of any farm. Ask a farm owner to invite you, then open the invitation link from your email.'
              }
            />
          )}
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
