import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Table, Spin, Empty, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  LineChart, Line,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import { reportsApi, type AnimalReport, type AnimalCountByCategory } from '../../api/reports';
import { getApiError } from '../../api/farmApi';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const AnimalReportsPage: React.FC = () => {const { t: translate } = useTranslation('reports'); 
  const [report, setReport] = useState<AnimalReport | null>(null);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    try {
      const res = await reportsApi.animalReport(from, to);
      setReport(res.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadData(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadData]);

  const typeColumns: ColumnsType<AnimalCountByCategory> = [
    { title: translate('type'), dataIndex: 'name' },
    { title: translate('count'), dataIndex: 'count', align: 'right' },
  ];

  const statusColumns: ColumnsType<AnimalCountByCategory> = [
    { title: translate('status'), dataIndex: 'name' },
    { title: translate('count'), dataIndex: 'count', align: 'right' },
  ];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="animal-report" title={translate('animalReport')} headers={['Type', 'Count', 'Mortality', 'Transfers']} rows={(report?.byType ?? []).map(t => [t.name, t.count, '', '']).concat([['Total', report?.totalCount ?? 0, report?.mortalityCount ?? 0, report?.transferCount ?? 0]])} /></div>}>
        <Text type="secondary">{translate('animalInventoryStatusBreakdownGrowthTrendsMortalityAnd')}</Text>
      </Card>

      <Spin spinning={loading}>
        {/* Summary Cards */}
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title={translate('totalAnimals')} value={report?.totalCount ?? 0} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={translate('mortalityCount')} value={report?.mortalityCount ?? 0} valueStyle={{ color: '#ff4d4f' }} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={translate('transfers')} value={report?.transferCount ?? 0} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={translate('animalTypes')} value={report?.byType.length ?? 0} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* By Type Pie */}
          <Col xs={24} lg={12}>
            <Card title={translate('animalsByType')} size="small">
              {report?.byType && report.byType.length > 0 ? (
                <BreakdownPieChart data={report.byType.map(i => ({ name: i.name, value: i.count }))} />
              ) : <Empty description={translate('noData')} />}
            </Card>
          </Col>

          {/* By Status Pie */}
          <Col xs={24} lg={12}>
            <Card title={translate('animalsByStatus')} size="small">
              {report?.byStatus && report.byStatus.length > 0 ? (
                <BreakdownPieChart data={report.byStatus.map(i => ({ name: i.name, value: i.count }))} innerRadius={50} />
              ) : <Empty description={translate('noData')} />}
            </Card>
          </Col>
        </Row>

        {/* Growth Trend */}
        <Card title={translate('weightGrowthTrend')} size="small" style={{ marginBottom: 16 }}>
          {report?.growthTrend && report.growthTrend.length > 0 ? (
            <ResponsiveContainer width="100%" height={260}>
              <LineChart data={report.growthTrend}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="month" tick={{ fontSize: 10 }} />
                <YAxis />
                <Tooltip />
                <Legend />
                <Line type="monotone" dataKey="avgWeight" name="Avg Weight (kg)" stroke="#1677ff" dot={false} />
              </LineChart>
            </ResponsiveContainer>
          ) : <Empty description={translate('noWeightDataAvailable')} />}
        </Card>

        {/* Data Tables */}
        <Row gutter={[16, 16]}>
          <Col xs={24} lg={12}>
            <Card title={translate('byType')} size="small">
              <Table rowKey="name" columns={typeColumns} dataSource={report?.byType ?? []} pagination={false} size="small" />
            </Card>
          </Col>
          <Col xs={24} lg={12}>
            <Card title={translate('byStatus')} size="small">
              <Table rowKey="name" columns={statusColumns} dataSource={report?.byStatus ?? []} pagination={false} size="small" />
            </Card>
          </Col>
        </Row>
      </Spin>
    </div>
  );
};

export default AnimalReportsPage;
