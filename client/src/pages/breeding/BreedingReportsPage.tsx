import { useState, useEffect, useCallback, useMemo } from 'react';
import { Card, Row, Col, Statistic, Table, Tag, DatePicker, Space, Empty, Spin, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import { breedingReportsApi, type BreedingReportFilter } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import { message } from 'antd';
import { formatDate } from '../../i18n/format';
import dayjs from 'dayjs';
import type { BreedingSummaryReport, BreedingTrendEntry, MethodDistributionEntry, SirePerformanceEntry, CalendarEventEntry } from '../../types';
import { useTranslation } from 'react-i18next';

const { RangePicker } = DatePicker;
const { Text } = Typography;

const PRIORITY_COLORS: Record<string, string> = {
  High: 'red',
  Medium: 'orange',
  Normal: 'blue',
};

export default function BreedingReportsPage() {const { t } = useTranslation('breeding'); 
  const [, setFilter] = useState<BreedingReportFilter>({});
  const [summary, setSummary] = useState<BreedingSummaryReport | null>(null);
  const [trend, setTrend] = useState<BreedingTrendEntry[]>([]);
  const [methods, setMethods] = useState<MethodDistributionEntry[]>([]);
  const [sires, setSires] = useState<SirePerformanceEntry[]>([]);
  const [calendar, setCalendar] = useState<CalendarEventEntry[]>([]);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (f: BreedingReportFilter = {}) => {
    setLoading(true);
    try {
      const [sumRes, trendRes, methodRes, sireRes, calRes] = await Promise.all([
        breedingReportsApi.summary(f),
        breedingReportsApi.trend(f),
        breedingReportsApi.methods(f),
        breedingReportsApi.sires(f),
        breedingReportsApi.calendar(60),
      ]);
      setSummary(sumRes.data);
      setTrend(trendRes.data);
      setMethods(methodRes.data);
      setSires(sireRes.data);
      setCalendar(calRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { loadData({}); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadData]);

  const handleDateChange = (dates: unknown) => {
    if (dates && Array.isArray(dates) && dates.length === 2) {
      const newFilter: BreedingReportFilter = {
        fromDate: dates[0]?.toISOString(),
        toDate: dates[1]?.toISOString(),
      };
      setFilter(newFilter);
      loadData(newFilter);
    } else {
      setFilter({});
      loadData({});
    }
  };

  const sireColumns: ColumnsType<SirePerformanceEntry> = [
    { title: t('sire'), key: 'sire', render: (_, r) => <><strong>{r.sireTagNumber}</strong>{r.sireName && <Text type="secondary"> ({r.sireName})</Text>}</> },
    { title: t('breedings'), dataIndex: 'totalBreedings', key: 'totalBreedings' },
    { title: t('confirmed'), dataIndex: 'confirmed', key: 'confirmed', render: (v: number) => <Tag color="green">{v}</Tag> },
    { title: t('failed'), dataIndex: 'failed', key: 'failed', render: (v: number) => <Tag color="red">{v}</Tag> },
    { title: t('pending'), dataIndex: 'pending', key: 'pending', render: (v: number) => <Tag>{v}</Tag> },
    { title: t('successRate'), dataIndex: 'successRate', key: 'successRate', render: (v: number) => <span style={{ color: v >= 60 ? '#52c41a' : v >= 30 ? '#faad14' : '#ff4d4f', fontWeight: 'bold' }}>{v}%</span> },
    { title: t('offspring'), dataIndex: 'totalOffspring', key: 'totalOffspring' },
  ];

  const calendarColumns: ColumnsType<CalendarEventEntry> = [
    {
      title: t('date'),
      dataIndex: 'eventDate',
      key: 'eventDate',
      render: (text: string) => formatDate(text),
      sorter: (a, b) => dayjs(a.eventDate).unix() - dayjs(b.eventDate).unix(),
      defaultSortOrder: 'ascend',
    },
    {
      title: t('event'),
      dataIndex: 'eventType',
      key: 'eventType',
      render: (text: string) => <Tag>{text}</Tag>,
    },
    {
      title: t('animal'),
      key: 'animal',
      render: (_, r) => <><strong>{r.animalTag}</strong>{r.animalName && <Text type="secondary"> ({r.animalName})</Text>}</>,
    },
    { title: t('details'), dataIndex: 'details', key: 'details', render: (text: string) => text || '-' },
    {
      title: t('priority'),
      dataIndex: 'priority',
      key: 'priority',
      render: (text: string) => <Tag color={PRIORITY_COLORS[text] || 'default'}>{text}</Tag>,
    },
  ];

  const pieData = methods.map(m => ({ name: m.method, value: m.count }));

  // Result distribution for the donut: colour lives on the datum so dropping a zero slice cannot shift the palette.
  const resultData = useMemo(() => [
    { name: 'Confirmed', value: summary?.confirmedCount ?? 0, color: '#52c41a' },
    { name: 'Pending', value: summary?.pendingCount ?? 0, color: '#faad14' },
    { name: 'Failed', value: summary?.failedCount ?? 0, color: '#ff4d4f' },
  ], [summary]);

  return (
    <Spin spinning={loading}>
      <Space style={{ marginBottom: 16 }}>
        <RangePicker onChange={handleDateChange} />
        <Text type="secondary">{t('filterByDateRangeLeaveEmptyForAll')}</Text>
      </Space>

      {/* Summary Stats */}
      {summary && (
        <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('totalBreedings')} value={summary.totalBreedingRecords} />
            </Card>
          </Col>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('confirmationRate')} value={summary.confirmationRate} suffix="%" valueStyle={{ color: summary.confirmationRate >= 50 ? '#52c41a' : '#ff4d4f' }} />
            </Card>
          </Col>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('activePregnancies')} value={summary.activePregnancies} valueStyle={{ color: '#1677ff' }} />
            </Card>
          </Col>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('totalBirths')} value={summary.totalBirths} />
            </Card>
          </Col>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('aliveOffspring')} value={summary.aliveOffspring} valueStyle={{ color: '#52c41a' }} />
            </Card>
          </Col>
          <Col xs={12} sm={6} md={4}>
            <Card size="small">
              <Statistic title={t('avgGestation')} value={summary.averageGestationDays} suffix="days" />
            </Card>
          </Col>
        </Row>
      )}

      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        {/* Monthly Trend Chart */}
        <Col xs={24} lg={14}>
          <Card title={t('monthlyBreedingTrend')} size="small">
            {trend.length === 0 ? (
              <Empty description={t('noData')} />
            ) : (
              <ResponsiveContainer width="100%" height={300}>
                <BarChart data={trend}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis allowDecimals={false} />
                  <Tooltip />
                  <Legend />
                  <Bar dataKey="count" name="Total" fill="#1677ff" />
                  <Bar dataKey="confirmed" name="Confirmed" fill="#52c41a" />
                  <Bar dataKey="failed" name="Failed" fill="#ff4d4f" />
                </BarChart>
              </ResponsiveContainer>
            )}
          </Card>
        </Col>

        {/* Method Distribution Pie Chart */}
        <Col xs={24} lg={10}>
          <Card title={t('breedingMethodDistribution')} size="small">
            {methods.length === 0 ? (
              <Empty description={t('noData')} />
            ) : (
              <BreakdownPieChart data={pieData} height={300} outerRadius={100} />
            )}
          </Card>
        </Col>
      </Row>

      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        {/* Sire Performance */}
        <Col xs={24} lg={14}>
          <Card title={t('sirePerformance')} size="small">
            <Table
              rowKey="sireId"
              columns={sireColumns}
              dataSource={sires}
              pagination={false}
              size="small"
            />
          </Card>
        </Col>

        {/* Result Distribution Donut */}
        <Col xs={24} lg={10}>
          <Card title={t('resultDistribution')} size="small">
            {/* Zero counts are dropped from the ring but kept in the legend, so "Failed 0 (0%)" stays visible. */}
            <BreakdownPieChart data={resultData} height={220} innerRadius={60} outerRadius={90} />
          </Card>
        </Col>
      </Row>

      {/* Calendar Events */}
      <Card title={t('upcomingEvents60Days')} size="small" style={{ marginBottom: 24 }}>
        <Table
          rowKey="id"
          columns={calendarColumns}
          dataSource={calendar}
          pagination={{ pageSize: 10 }}
          size="small"
        />
      </Card>
    </Spin>
  );
}
