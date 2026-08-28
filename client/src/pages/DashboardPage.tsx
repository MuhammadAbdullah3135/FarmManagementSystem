import React, { useCallback, useEffect, useState } from 'react';
import {
  Card, Row, Col, Statistic, Alert, Typography, Space, Spin, Empty,
} from 'antd';
import {
  BugOutlined, HeartOutlined, MedicineBoxOutlined, CheckSquareOutlined,
  NodeIndexOutlined, InboxOutlined, CoffeeOutlined, WarningOutlined,
} from '@ant-design/icons';
import {
  BarChart, Bar, LineChart, Line, AreaChart, Area, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import DateRangeFilter from '../components/DateRangeFilter';
import {
  dashboardApi,
  type DashboardSummary,
  type DashboardAlert,
  type DashboardCharts,
} from '../api/dashboard';
import { getApiError } from '../api/farmApi';
import { message } from 'antd';

const { Title, Text } = Typography;

const PIE_COLORS = ['#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2', '#eb2f96', '#fa8c16'];

const formatCurrency = (v: number) =>
  `$${v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const DashboardPage: React.FC = () => {
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

  return (
    <div>
      <Title level={3}>Welcome, {user?.firstName}!</Title>
      <Text type="secondary">
        {activeFarm ? `Currently managing: ${activeFarm.name}` : 'Select a farm to get started'}
      </Text>

      {/* ── Summary Cards ──────────────────────────────────── */}
      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginTop: 24 }}>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Total Animals"
                value={summary?.totalAnimals ?? 0}
                prefix={<BugOutlined />}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Pregnant"
                value={summary?.pregnantCount ?? 0}
                prefix={<HeartOutlined />}
                valueStyle={{ color: '#1677ff' }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Sick"
                value={summary?.sickCount ?? 0}
                prefix={<MedicineBoxOutlined />}
                valueStyle={{ color: summary?.sickCount ? '#ff4d4f' : undefined }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Due Vaccination"
                value={summary?.dueVaccinationCount ?? 0}
                prefix={<MedicineBoxOutlined />}
                valueStyle={{ color: summary?.dueVaccinationCount ? '#faad14' : undefined }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Overdue Tasks"
                value={summary?.overdueTasks ?? 0}
                prefix={<CheckSquareOutlined />}
                valueStyle={{ color: summary?.overdueTasks ? '#ff4d4f' : undefined }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Upcoming Births"
                value={summary?.upcomingBirths ?? 0}
                prefix={<NodeIndexOutlined />}
                valueStyle={{ color: summary?.upcomingBirths ? '#52c41a' : undefined }}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Feed Stock Value"
                value={summary?.totalFeedStockValue ?? 0}
                prefix={<CoffeeOutlined />}
                formatter={(value) => formatCurrency(value as number)}
              />
            </Card>
          </Col>
          <Col xs={12} sm={8} md={6}>
            <Card>
              <Statistic
                title="Inventory Value"
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
        <Card title="Alerts" style={{ marginTop: 16 }}>
          <Space direction="vertical" style={{ width: '100%' }}>
            {criticalAlerts.map((alert, i) => (
              <Alert
                key={`crit-${i}`}
                type="error"
                showIcon
                icon={<WarningOutlined />}
                message={alert.title}
                description={alert.message}
                closable
              />
            ))}
            {warningAlerts.map((alert, i) => (
              <Alert
                key={`warn-${i}`}
                type="warning"
                showIcon
                message={alert.title}
                description={alert.message}
                closable
              />
            ))}
            {infoAlerts.map((alert, i) => (
              <Alert
                key={`info-${i}`}
                type="info"
                showIcon
                message={alert.title}
                description={alert.message}
                closable
              />
            ))}
          </Space>
        </Card>
      )}

      {/* ── Charts ─────────────────────────────────────────── */}
      <Card
        title="Charts"
        style={{ marginTop: 16 }}
        extra={<DateRangeFilter onChange={(from, to) => loadCharts(from, to)} />}
      >
        <Spin spinning={chartLoading}>
          <Row gutter={[16, 16]}>
            {/* Animal Trends */}
            <Col xs={24} lg={12}>
              <Card title="Animal Additions" size="small">
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
                  <Empty description="No data" />
                )}
              </Card>
            </Col>

            {/* Expense Breakdown */}
            <Col xs={24} lg={12}>
              <Card title="Expense Breakdown" size="small">
                {charts?.expenseBreakdown && charts.expenseBreakdown.items.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <PieChart>
                      <Pie
                        data={charts.expenseBreakdown.items.map(i => ({ name: i.categoryName, value: i.total }))}
                        cx="50%"
                        cy="50%"
                        outerRadius={90}
                        label={({ name, percent }) => `${name} (${((percent ?? 0) * 100).toFixed(0)}%)`}
                        dataKey="value"
                      >
                        {charts.expenseBreakdown.items.map((_, index) => (
                          <Cell key={`cell-${index}`} fill={PIE_COLORS[index % PIE_COLORS.length]} />
                        ))}
                      </Pie>
                      <Tooltip formatter={(value: number) => formatCurrency(value)} />
                    </PieChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty description="No data" />
                )}
              </Card>
            </Col>

            {/* Feed Consumption */}
            <Col xs={24} lg={12}>
              <Card title="Feed Consumption Trend" size="small">
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
                  <Empty description="No data" />
                )}
              </Card>
            </Col>

            {/* Monthly P/L */}
            <Col xs={24} lg={12}>
              <Card title="Monthly Profit & Loss" size="small">
                {charts?.monthlyPL && charts.monthlyPL.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <BarChart data={charts.monthlyPL}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="monthName" tick={{ fontSize: 11 }} />
                      <YAxis />
                      <Tooltip formatter={(value: number) => formatCurrency(value)} />
                      <Legend />
                      <Bar dataKey="income" name="Income" fill="#52c41a" />
                      <Bar dataKey="expenses" name="Expenses" fill="#ff4d4f" />
                    </BarChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty description="No data" />
                )}
              </Card>
            </Col>
          </Row>
        </Spin>
      </Card>

      {/* ── No farm selected ───────────────────────────────── */}
      {!activeFarm && (
        <Card style={{ marginTop: 24 }}>
          <Text>Select a farm from the dropdown in the header to start managing your operations.</Text>
        </Card>
      )}
    </div>
  );
};

export default DashboardPage;
