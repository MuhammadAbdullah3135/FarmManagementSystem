import React, { useCallback, useEffect, useState } from 'react';
import {
  Card, Row, Col, Statistic, Alert, Typography, Space, Spin, Empty,
} from 'antd';
import {
  BugOutlined, HeartOutlined, MedicineBoxOutlined, CheckSquareOutlined,
  NodeIndexOutlined, InboxOutlined,  CoffeeOutlined,
} from '@ant-design/icons';
import {
  BarChart, Bar, LineChart, Line, AreaChart, Area,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import { Link } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import BreakdownPieChart from '../components/BreakdownPieChart';
import DateRangeFilter from '../components/DateRangeFilter';
import {
  dashboardApi,
  type DashboardSummary,
  type DashboardAlert,
  type DashboardCharts,
} from '../api/dashboard';
import { getApiError } from '../api/farmApi';
import { formatMoney } from '../i18n/format';
import { renderKeyedMessage } from '../i18n/serverMessage';
import { message } from 'antd';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

// One money formatter for the whole app (see `i18n/format.ts`): the `$` is unchanged, the
// separators follow the language. Wrapped rather than aliased so recharts' extra callback
// arguments cannot be mistaken for a locale.
const formatCurrency = (v: number) => formatMoney(v);

/**
 * Renders an alert's `link` as a navigable action. Alerts without a link keep
 * the plain layout (no dead control), so this returns `undefined` for them.
 */
/**
 * An alert's action, translated: the link is the server's route, the word is the reader's.
 * An alert with no route keeps no action rather than a control that goes nowhere.
 */
const alertAction = (alert: DashboardAlert, viewLabel: string) =>
  alert.link ? <Link to={alert.link}>{viewLabel}</Link> : undefined;

/** How many alert cards the dashboard shows before it counts the rest. */
const ALERT_PREVIEW_LIMIT = 5;

const DashboardPage: React.FC = () => {const { t } = useTranslation('dashboard'); 
  const { user } = useAuthStore();
  const { activeFarm } = useFarmStore();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [alerts, setAlerts] = useState<DashboardAlert[]>([]);
  const [charts, setCharts] = useState<DashboardCharts | null>(null);
  const [loading, setLoading] = useState(true);
  const [chartLoading, setChartLoading] = useState(true);

  const loadSummary = useCallback(async () => {
    setLoading(true);
    try {
      const [sumRes, alertRes] = await Promise.all([
        dashboardApi.summary(),
        dashboardApi.alerts(),
      ]);
      setSummary(sumRes.data);
      setAlerts(alertRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  const loadCharts = useCallback(async (from?: string, to?: string) => {
    setChartLoading(true);
    try {
      const res = await dashboardApi.charts(from, to);
      setCharts(res.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setChartLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSummary();
    loadCharts();
  }, [loadSummary, loadCharts]);

  const criticalAlerts = alerts.filter(a => a.severity === 'Critical');
  const warningAlerts = alerts.filter(a => a.severity === 'Warning');
  const infoAlerts = alerts.filter(a => a.severity === 'Info');

  /**
   * Most severe first, then a preview rather than the lot: one farm's alert set is dominated by
   * the same condition repeated per animal (33 alerts on the QA farm, nearly all of them the
   * same overdue vaccination), which buried the summary cards and the charts under a wall of
   * identical cards. The remainder is counted and linked instead of dropped.
   */
  const sortedAlerts = [...criticalAlerts, ...warningAlerts, ...infoAlerts];
  const previewAlerts = sortedAlerts.slice(0, ALERT_PREVIEW_LIMIT);
  const hiddenAlertCount = sortedAlerts.length - previewAlerts.length;

  return (
    <div>
      <Title level={3}>{t('welcome')} {user?.firstName}!</Title>
      <Text type="secondary">
        {activeFarm ? `Currently managing: ${activeFarm.name}` : t('selectAFarmToGetStarted')}
      </Text>

      {/* ── Summary Cards ──────────────────────────────────── */}
      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginTop: 24 }}>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('totalAnimals')}
                value={summary?.totalAnimals ?? 0}
                prefix={<BugOutlined />}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('pregnant')}
                value={summary?.pregnantCount ?? 0}
                prefix={<HeartOutlined />}
                styles={{ content: { color: '#1677ff' } }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('sick')}
                value={summary?.sickCount ?? 0}
                prefix={<MedicineBoxOutlined />}
                styles={{ content: { color: summary?.sickCount ? '#ff4d4f' : undefined } }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('dueWeightChecks')}
                value={summary?.dueWeightCheckCount ?? 0}
                prefix={<MedicineBoxOutlined />}
                styles={{ content: { color: summary?.dueWeightCheckCount ? '#faad14' : undefined } }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('overdueTasks')}
                value={summary?.overdueTasks ?? 0}
                prefix={<CheckSquareOutlined />}
                styles={{ content: { color: summary?.overdueTasks ? '#ff4d4f' : undefined } }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('upcomingBirths')}
                value={summary?.upcomingBirths ?? 0}
                prefix={<NodeIndexOutlined />}
                styles={{ content: { color: summary?.upcomingBirths ? '#52c41a' : undefined } }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('feedStockValue')}
                value={summary?.totalFeedStockValue ?? 0}
                prefix={<CoffeeOutlined />}
                formatter={(value) => formatCurrency(value as number)}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title={t('inventoryValue')}
                value={summary?.totalInventoryStockValue ?? 0}
                prefix={<InboxOutlined />}
                formatter={(value) => formatCurrency(value as number)}
              />
            </Card>
          </Col>
        </Row>
      </Spin>

      {/* ── Alerts ─────────────────────────────────────────── */}
      {alerts.length > 0 && (
        <Card title={`${t('alerts')} (${sortedAlerts.length})`} style={{ marginTop: 16 }}>
          <Space orientation="vertical" style={{ width: '100%' }}>
            {previewAlerts.map((alert, i) => (
              <Alert
                key={`${alert.severity}-${i}`}
                /* Severity keeps antd's own icon: overriding it with a single warning
                   triangle made every Critical alert look like every other one. */
                type={alert.severity === 'Critical' ? 'error' : alert.severity === 'Warning' ? 'warning' : 'info'}
                showIcon
                // The keyed form when the server sent one, the English text otherwise —
                // the same preference the rest of the client uses for server messages.
                title={renderKeyedMessage(alert.titleKey, alert.titleArgs, alert.title)}
                description={renderKeyedMessage(alert.messageKey, alert.messageArgs, alert.message)}
                action={alertAction(alert, t('view'))}
                closable
              />
            ))}
            {hiddenAlertCount > 0 && (
              <Text type="secondary">
                {hiddenAlertCount} {t('moreAlert')}{hiddenAlertCount === 1 ? '' : 's'} {t('onThisFarm')}{' '}
                <Link to="/dashboard/notifications">{t('seeThemAll')}</Link>.
              </Text>
            )}
          </Space>
        </Card>
      )}

      {/* ── Charts ─────────────────────────────────────────── */}
      <Card
        title={t('charts')}
        style={{ marginTop: 16 }}
        extra={<DateRangeFilter onChange={(from, to) => loadCharts(from, to)} />}
      >
        <Spin spinning={chartLoading}>
          <Row gutter={[16, 16]}>
            {/* Animal Trends */}
            <Col xs={24} lg={12}>
              <Card title={t('animalAdditions')} size="small">
                {charts?.animalTrends && charts.animalTrends.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <AreaChart data={charts.animalTrends}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                      <YAxis allowDecimals={false} />
                      <Tooltip />
                      <Area type="monotone" dataKey="count" name="Animals Added" fill="#1677ff" fillOpacity={0.3} stroke="#1677ff" />
                    </AreaChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty description={t('noData')} />
                )}
              </Card>
            </Col>

            {/* Expense Breakdown */}
            <Col xs={24} lg={12}>
              <Card title={t('expenseBreakdown')} size="small">
                {charts?.expenseBreakdown && charts.expenseBreakdown.items.length > 0 ? (
                  <BreakdownPieChart
                    data={charts.expenseBreakdown.items.map(i => ({ name: i.categoryName, value: i.total }))}
                    valueFormatter={formatCurrency}
                  />
                ) : (
                  <Empty description={t('noData')} />
                )}
              </Card>
            </Col>

            {/* Feed Consumption */}
            <Col xs={24} lg={12}>
              <Card title={t('feedConsumptionTrend')} size="small">
                {charts?.feedConsumptionTrend && charts.feedConsumptionTrend.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <LineChart data={charts.feedConsumptionTrend}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="label" tick={{ fontSize: 10 }} />
                      <YAxis />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="quantity" name="Quantity" stroke="#1677ff" dot={false} />
                      <Line type="monotone" dataKey="cost" name="Cost" stroke="#52c41a" dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty description={t('noData')} />
                )}
              </Card>
            </Col>

            {/* Monthly P/L */}
            <Col xs={24} lg={12}>
              <Card title={t('monthlyProfitLoss')} size="small">
                {charts?.monthlyPL && charts.monthlyPL.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <BarChart data={charts.monthlyPL}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="monthName" tick={{ fontSize: 11 }} />
                      <YAxis />
                      <Tooltip formatter={(value) => formatCurrency(Number(value ?? 0))} />
                      <Legend />
                      <Bar dataKey="income" name="Income" fill="#52c41a" />
                      <Bar dataKey="expenses" name="Expenses" fill="#ff4d4f" />
                    </BarChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty description={t('noData')} />
                )}
              </Card>
            </Col>
          </Row>
        </Spin>
      </Card>

      {/* ── No farm selected ───────────────────────────────── */}
      {!activeFarm && (
        <Card style={{ marginTop: 24 }}>
          <Text>{t('selectAFarmFromTheDropdownInThe')}</Text>
        </Card>
      )}
    </div>
  );
};

export default DashboardPage;
