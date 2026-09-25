import React, { useEffect, useState } from 'react';
import '../AppLayout.css';
import { Layout, Menu, Typography, Dropdown, Avatar, Badge, Button, Drawer, Result, Space, Spin, Tooltip, message, Modal } from 'antd';
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
  CloudSyncOutlined,
  DownloadOutlined,
  TranslationOutlined,
  CheckOutlined,
} from '@ant-design/icons';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { useOfflineStore } from '../offline/connectivity';
import { useSyncStore } from '../offline/syncStatus';
import { clearAllOfflineData, clearOfflineDataForFarm } from '../offline/offlineData';
import OfflineBanner from './OfflineBanner';
import RouteErrorBoundary from './RouteErrorBoundary';
import { filterMenuByRole } from '../utils/permissions';
import type { AppMenuItem } from '../utils/permissions';
import { usePermissions } from '../hooks/usePermissions';
import { setFarmAccessDeniedHandler } from '../api/axios';
import { notificationsApi } from '../api/notifications';
import type { MenuProps } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { changeLocale } from '../i18n';
import { LOCALE_LABELS, SUPPORTED_LOCALES, type Locale } from '../i18n/locale';
import { refreshAccountLocale, saveAccountLocale } from '../i18n/localeSync';

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
 *
 * Labels are keys (`nav:*`) rather than text: the tree is language-free, and
 * {@link localizeMenuItems} resolves each one for the active language.
 */
export const appMenuItems: AppMenuItem[] = [
  {
    key: '/dashboard',
    icon: <DashboardOutlined />,
    labelKey: 'nav:dashboard',
  },
  {
    key: 'feed',
    icon: <MedicineBoxOutlined />,
    labelKey: 'nav:feedManagement',
    children: [
      { key: '/dashboard/feed/types', icon: <CoffeeOutlined />, labelKey: 'nav:feedTypes' },
      { key: '/dashboard/feed/records', icon: <UnorderedListOutlined />, labelKey: 'nav:feedRecords' },
      { key: '/dashboard/feed/diet-plans', labelKey: 'nav:dietPlans' },
      { key: '/dashboard/feed/schedules', labelKey: 'nav:feedingSchedules' },
      { key: '/dashboard/feed/tasks', labelKey: 'nav:feedingTasks' },
      { key: '/dashboard/feed/reports', labelKey: 'nav:reports' },
    ],
  },
  {
    key: '/dashboard/tasks',
    icon: <CheckSquareOutlined />,
    labelKey: 'nav:farmTasks',
  },
  {
    // Ungated on purpose: every member can read their *own* notifications, and
    // the dispatcher already withholds alerts a role cannot act on, so there is
    // no module for a menu filter to check.
    key: '/dashboard/notifications',
    icon: <BellOutlined />,
    labelKey: 'nav:notifications',
  },
  {
    key: 'animals',
    icon: <BugOutlined />,
    labelKey: 'nav:animals',
    children: [
      { key: '/dashboard/animals', icon: <UnorderedListOutlined />, labelKey: 'nav:animalList' },
    ],
  },
  {
    key: 'breeding',
    icon: <NodeIndexOutlined />,
    labelKey: 'nav:breeding',
    children: [
      { key: '/dashboard/breeding/records', icon: <UnorderedListOutlined />, labelKey: 'nav:breedingRecords' },
      { key: '/dashboard/breeding/gestation', icon: <HeartOutlined />, labelKey: 'nav:gestationTracking' },
      { key: '/dashboard/breeding/births', icon: <BugOutlined />, labelKey: 'nav:birthRecords' },
      { key: '/dashboard/breeding/lineage', icon: <NodeIndexOutlined />, labelKey: 'nav:lineageView' },
      { key: '/dashboard/breeding/reports', icon: <BarChartOutlined />, labelKey: 'nav:reportsAnalytics' },
    ],
  },
  {
    key: 'hr',
    icon: <TeamOutlined />,
    labelKey: 'nav:hr',
    children: [
      { key: '/dashboard/hr/employees', labelKey: 'nav:employees' },
      { key: '/dashboard/hr/departments-roles', labelKey: 'nav:departmentsRoles' },
      { key: '/dashboard/hr/salary-payments', icon: <DollarOutlined />, labelKey: 'nav:salaryPayroll' },
      { key: '/dashboard/hr/attendance', icon: <ClockCircleOutlined />, labelKey: 'nav:attendance' },
      { key: '/dashboard/hr/performance', icon: <StarOutlined />, labelKey: 'nav:performanceReviews' },
    ],
  },
  {
    key: 'finance',
    icon: <DollarOutlined />,
    labelKey: 'nav:finance',
    children: [
      { key: '/dashboard/finance/expenses', icon: <AuditOutlined />, labelKey: 'nav:expenses' },
      { key: '/dashboard/finance/incomes', icon: <AuditOutlined />, labelKey: 'nav:income' },
      { key: '/dashboard/finance/reports', icon: <BarChartOutlined />, labelKey: 'nav:reports' },
      { key: '/dashboard/finance/categories', icon: <UnorderedListOutlined />, labelKey: 'nav:categoriesPaymentMethods' },
    ],
  },
  {
    key: 'inventory',
    icon: <InboxOutlined />,
    labelKey: 'nav:inventory',
    children: [
      { key: '/dashboard/inventory/items', icon: <UnorderedListOutlined />, labelKey: 'nav:inventoryItems', requiredModule: 'inventory.items' },
      { key: '/dashboard/inventory/movements', icon: <SwapOutlined />, labelKey: 'nav:stockMovements', requiredModule: 'inventory.items' },
      { key: '/dashboard/inventory/suppliers', icon: <TeamOutlined />, labelKey: 'nav:suppliers', requiredModule: 'inventory.suppliers' },
      { key: '/dashboard/inventory/customers', icon: <TeamOutlined />, labelKey: 'nav:customers', requiredModule: 'inventory.customers' },
      { key: '/dashboard/inventory/reports', icon: <BarChartOutlined />, labelKey: 'nav:reports', requiredModule: 'inventory.items' },
    ],
  },
  {
    key: 'health',
    icon: <HeartOutlined />,
    labelKey: 'nav:healthManagement',
    children: [
      { key: '/dashboard/health/medical', icon: <MedicineBoxOutlined />, labelKey: 'nav:medicalRecords' },
      { key: '/dashboard/health/medicines', icon: <MedicineBoxOutlined />, labelKey: 'nav:medicines' },
      { key: '/dashboard/health/medicines/alerts', icon: <MedicineBoxOutlined />, labelKey: 'nav:medicineAlerts' },
      { key: '/dashboard/health/vaccines', labelKey: 'nav:vaccineTypes' },
      { key: '/dashboard/health/vaccinations', labelKey: 'nav:vaccinationRecords' },
      { key: '/dashboard/health/vaccinations/schedule', labelKey: 'nav:vaccinationSchedules' },
      { key: '/dashboard/health/weight-schedules', labelKey: 'nav:weightCheckSchedules' },
      { key: '/dashboard/health/costs', labelKey: 'nav:vetCosts' },
    ],
  },
  {
    key: 'reports',
    icon: <BarChartOutlined />,
    labelKey: 'nav:reports',
    children: [
      { key: '/dashboard/reports/animals', labelKey: 'nav:animalReports' },
      { key: '/dashboard/reports/financial', labelKey: 'nav:financialReports' },
      { key: '/dashboard/reports/feed', labelKey: 'nav:feedReports' },
      { key: '/dashboard/reports/medical', labelKey: 'nav:medicalReports' },
      { key: '/dashboard/reports/vaccination', labelKey: 'nav:vaccinationReports' },
      { key: '/dashboard/reports/breeding', labelKey: 'nav:breedingReports' },
      { key: '/dashboard/reports/employees', labelKey: 'nav:employeeReports' },
      { key: '/dashboard/reports/cost-per-animal', labelKey: 'nav:costPerAnimal' },
    ],
  },
  {
    key: 'configuration',
    icon: <SettingOutlined />,
    labelKey: 'nav:configuration',
    children: [
      { key: '/dashboard/configuration', icon: <SettingOutlined />, labelKey: 'nav:farmConfiguration' },
      { key: '/dashboard/farm/members', icon: <TeamOutlined />, labelKey: 'nav:members', requiredModule: 'farm.members' },
      { key: '/dashboard/configuration/export', icon: <DownloadOutlined />, labelKey: 'nav:dataExport', requiredModule: 'data.export' },
    ],
  },
  {
    key: 'admin',
    icon: <AuditOutlined />,
    labelKey: 'nav:admin',
    children: [
      { key: '/dashboard/admin/audit-log', icon: <AuditOutlined />, labelKey: 'nav:auditLog', requiredModule: 'admin.audit-log' },
      { key: '/dashboard/admin/jobs', icon: <ClockCircleOutlined />, labelKey: 'nav:scheduledJobs', requiredModule: 'admin.jobs' },
    ],
  },
];

/** Resolves the `labelKey` of every entry (and its children) for the active language. */
export const localizeMenuItems = (items: AppMenuItem[], t: TFunction): AppMenuItem[] =>
  items.map((item) => ({
    ...item,
    label: item.labelKey ? t(item.labelKey) : item.label,
    children: item.children ? localizeMenuItems(item.children, t) : undefined,
  }));

/**
 * Routes that are account-scoped rather than farm-scoped.
 *
 * The content area normally waits for an active farm, because almost every
 * screen is farm data. The job-status view is infrastructure, not farm data
 * (its endpoint is /api/admin/jobs, with no farm in the path), so it must stay
 * reachable even when the user has no farm selected.
 */
const farmIndependentPaths = [
  '/dashboard/admin/jobs',
  // The queue belongs to the account, not to the farm being viewed: items queued against a
  // farm whose access was removed still have to be visible and actionable.
  '/dashboard/sync',
];

const isFarmIndependent = (pathname: string) =>
  farmIndependentPaths.some(
    (path) => pathname === path || pathname.startsWith(`${path}/`),
  );  const AppLayout: React.FC = () => {const { t, i18n } = useTranslation('common');  
  const navigate = useNavigate();
  const location = useLocation();
  const { user, logout } = useAuthStore();
  const {
    farms, activeFarm, fetchFarms, setActiveFarm, clearActiveFarm,
    isLoading: isLoadingFarms,
  } = useFarmStore();
  const { roles } = usePermissions();

  // Normalised because i18next may report a regional tag (`es-MX`) that the switcher
  // must still match to the `es` entry it offers.
  const activeLocale = i18n.language.split('-')[0];

  const [isMobile, setIsMobile] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('screen and (max-width: 991.98px)').matches,
  );
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [unreadNotifications, setUnreadNotifications] = useState(0);

  // What this device has stored, for the "clear offline data" confirmation. Subscribed
  // as a single field (not a derived object) so the reference stays stable between
  // updates, which is what zustand v5 compares.
  const offlineStats = useOfflineStore((store) => store.stats);

  // The queue, for the header badge and the sign-out warning. Scalar selectors, for the same
  // stability reason.
  const pendingCount = useSyncStore((store) => store.pendingCount);
  const quarantinedCount = useSyncStore((store) => store.quarantinedCount);
  const unsyncedCount = pendingCount + quarantinedCount;

  useEffect(() => {
    fetchFarms();
  }, [fetchFarms]);

  // Once per session: the account's language may have been changed on another device since
  // this one cached a copy. Silent by design — an offline boot, or a server that predates
  // the preference, must leave the page exactly as it is.
  useEffect(() => {
    void refreshAccountLocale().catch(() => undefined);
  }, []);

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
      const accountId = useAuthStore.getState().user?.accountId;
      const lostFarmId = useFarmStore.getState().activeFarm?.id ?? localStorage.getItem('activeFarmId');

      // Drop that farm's cached data too. Keeping rows the API has just refused to
      // serve would mean the device keeps showing a farm its user can no longer open,
      // and there is no offline path left that legitimately needs them.
      if (accountId && lostFarmId) {
        void clearOfflineDataForFarm(accountId, lostFarmId);
      }

      clearActiveFarm();
      void fetchFarms();
      message.warning(t('yourAccessToThatFarmWasRemoved'));
    });

    return () => setFarmAccessDeniedHandler(null);
  }, [clearActiveFarm, fetchFarms, t]);

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
    // Signing out clears the cached read data but *keeps* the queue: a measurement that only
    // exists on this device is not something to throw away on the way out. The user is told
    // exactly that, because "sign out" usually means "the device stops mattering".
    if (pendingCount > 0) {
      Modal.confirm({
        title: t('signOutWithUnsyncedRecords'),
        content: `${pendingCount} record${pendingCount === 1 ? '' : 's'} on this device ${pendingCount === 1 ? 'has' : 'have'} not reached the server yet. They stay on this device and are sent the next time this account signs in.`,
        okText: t('signOutAnyway'),
        okButtonProps: { danger: true },
        cancelText: t('staySignedIn'),
        onOk: () => {
          logout();
          navigate('/login');
        },
      });
      return;
    }

    // logout() clears the session and the selected farm; navigating afterwards keeps the
    // login screen reachable even if a cached page would otherwise re-render first.
    logout();
    navigate('/login');
  };

  /**
   * The user-facing "clear offline data" action.
   *
   * Confirmed rather than one-click: it is the only way to remove farm data from the
   * device short of signing out, and a misplaced tap should not quietly do that.
   */
  const handleClearOfflineData = () => {
    const { recordCount, collectionCount } = offlineStats;
    const queuedWarning = pendingCount > 0
      ? ` It also discards ${pendingCount} unsynced record${pendingCount === 1 ? '' : 's'} that ${pendingCount === 1 ? 'has' : 'have'} never reached the server. That cannot be undone.`
      : '';

    Modal.confirm({
      title: t('clearOfflineData'),
      // The queue exists now, so this copy has to say what clearing it costs. A cached row is
      // fetched again next time the device is online; a queued one is simply gone.
      content: recordCount > 0 || pendingCount > 0
        ? `This device has ${recordCount} cached record${recordCount === 1 ? '' : 's'} across ${collectionCount} collection${collectionCount === 1 ? '' : 's'}. Clearing removes them; the app fetches them again next time you are online.${queuedWarning}`
        : 'This device has no cached records. Nothing will change.',
      okText: t('clear'),
      okButtonProps: { danger: true },
      onOk: async () => {
        const cleared = await clearAllOfflineData();
        message.success(
          cleared.recordCount > 0 || cleared.queuedCount > 0
            ? `Cleared ${cleared.recordCount} cached record${cleared.recordCount === 1 ? '' : 's'} and discarded ${cleared.queuedCount} unsynced record${cleared.queuedCount === 1 ? '' : 's'}.`
            : 'Nothing to clear.',
        );
      },
    });
  };

  const farmMenuItems = farms.map((farm) => ({
    key: farm.id,
    label: farm.name,
    onClick: () => setActiveFarm(farm),
  }));

  /**
   * Switches language everywhere, then remembers it on the account.
   *
   * The switch happens first and unconditionally: the device already has the choice and
   * the whole UI is bundled, so it applies on the next render. Only the *saving* can fail,
   * and that is reported as a warning rather than rolled back — the user asked for Spanish
   * and Spanish is what they should get, even if the account could not be told.
   */
  const handleLocaleChange = async (locale: Locale) => {
    if (locale === activeLocale) return;
    changeLocale(locale);
    try {
      await saveAccountLocale(locale);
    } catch {
      message.warning(t('languageSaveFailed'));
    }
  };

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
      key: 'language',
      icon: <TranslationOutlined />,
      label: t('language'),
      children: SUPPORTED_LOCALES.map((locale) => ({
        key: `locale-${locale}`,
        // Named in its own language, and ticked when it is the one in use.
        label: LOCALE_LABELS[locale],
        icon: locale === activeLocale ? <CheckOutlined /> : null,
        onClick: () => void handleLocaleChange(locale),
      })),
    },
    {
      key: 'offline-sync',
      label: unsyncedCount > 0 ? `${t('offlineSync')} (${unsyncedCount})` : t('offlineSync'),
      onClick: () => navigate('/dashboard/sync'),
    },
    {
      key: 'offline-data',
      label: t('clearOfflineData2'),
      onClick: handleClearOfflineData,
    },
    {
      type: 'divider' as const,
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: t('logout'),
      onClick: handleLogout,
    },
  ];

  const menuItems = filterMenuByRole(localizeMenuItems(appMenuItems, t), roles);

  const renderNav = (onClose?: () => void) => (
    <>
      <div style={{ padding: '16px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <Text strong style={{ color: '#fff', fontSize: 18 }}>{t('fms')}</Text>
        {onClose && (
          <Button
            type="text"
            size="small"
            icon={<CloseOutlined />}
            onClick={onClose}
            aria-label={t('closeMenu')}
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
                aria-label={t('openMenu')}
              />
            )}
            <Dropdown menu={{ items: farmMenuItems }} trigger={['click']}>
              <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                <SwapOutlined />
                {activeFarm ? (
                  <Text strong style={{ textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap', maxWidth: isMobile ? 120 : undefined }}>{activeFarm.name}</Text>
                ) : farms.length > 0 ? (
                  <Text type="secondary">{t('selectAFarm')}</Text>
                ) : (
                  <Text type="secondary">{t('noFarmsYet')}</Text>
                )}
              </div>
            </Dropdown>
          </div>

          <Space size={4} style={{ minWidth: 0 }}>
            {/*
              The queue, at a glance: what is waiting and what the server refused. Red when a
              record needs a person (a refusal never resolves itself), otherwise a plain count.
            */}
            <Tooltip
              title={
                unsyncedCount === 0
                  ? t('offlineSync')
                  : [
                      t('waitingToSyncCount', { count: pendingCount }),
                      quarantinedCount > 0
                        ? t('refusedByServerCount', { count: quarantinedCount })
                        : null,
                    ]
                      .filter(Boolean)
                      .join(', ')
              }
            >
              <Button
                type="text"
                aria-label={t('offlineAndSync')}
                onClick={() => navigate('/dashboard/sync')}
              >
                <Badge count={unsyncedCount} size="small" overflowCount={99}>
                  <CloudSyncOutlined
                    style={{ fontSize: 16, color: quarantinedCount > 0 ? '#cf1322' : undefined }}
                  />
                </Badge>
              </Button>
            </Tooltip>

            <Button
              type="text"
              aria-label={t('notifications')}
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
          {/* Outside the farm/pages branch on purpose: connectivity is a property of the
              device, so the banner is present even while no farm is selected. */}
          <OfflineBanner />

          {activeFarm || isFarmIndependent(stripBase(location.pathname)) ? (
            /* Keyed by route: a screen that failed stays failed only while you are on it, so
               navigating anywhere else clears the error instead of showing it forever. */
            <RouteErrorBoundary routePath={stripBase(location.pathname)} key={stripBase(location.pathname)}>
              <Outlet />
            </RouteErrorBoundary>
          ) : isLoadingFarms ? (
            <div style={{ textAlign: 'center', padding: 48 }}>
              <Spin />
            </div>
          ) : (
            <Result
              status="info"
              title={farms.length > 0 ? t('selectAFarmToContinue') : t('noFarmAccess')}
              subTitle={
                farms.length > 0
                  ? t('chooseAFarmFromTheSelectorInThe')
                  : t('youAreNotAMemberOfAnyFarm')
              }
            />
          )}
        </Content>
      </Layout>

      <Drawer
        placement="left"
        open={mobileMenuOpen}
        onClose={() => setMobileMenuOpen(false)}
        size={240}
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
