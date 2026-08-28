import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Table, Spin, Empty, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  BarChart, Bar, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import { reportsApi, type MedicalReport, type MedicalByVet } from '../../api/reports';
import { getApiError } from '../../api/farmApi';

const { Text } = Typography;
const PIE_COLORS = ['#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2'];

const formatCurrency = (v: number) =>
  `$${v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const MedicalReportsPage: React.FC = () => {
  const [report, setReport] = useState<MedicalReport | null>(null);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    try {
      const res = await reportsApi.medicalReport(from, to);
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

  const vetColumns: ColumnsType<MedicalByVet> = [
    { title: 'Veterinarian', dataIndex: 'vetName' },
    { title: 'Cases', dataIndex: 'caseCount', align: 'right' },
    { title: 'Total Cost', dataIndex: 'totalCost', align: 'right', render: (v: number) => formatCurrency(v) },
  ];

  const statusData = report?.byStatus?.map(s => ({ name: s.status, value: s.count })) ?? [];
  const vetData = report?.byVet?.map(v => ({ name: v.vetName, value: v.totalCost })) ?? [];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="medical-report" title="Medical Report" headers={['Vet', 'Cases', 'Cost']} rows={(report?.byVet ?? []).map(v => [v.vetName, v.caseCount, v.totalCost])} /></div>}>
        <Text type="secondary">Treatment cases, costs by veterinarian, medicine usage</Text>
      </Card>

      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={8}><Card><Statistic title="Total Cases" value={report?.totalCases ?? 0} /></Card></Col>
          <Col xs={12} sm={8}><Card><Statistic title="Total Cost" value={report?.totalCost ?? 0} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={8}><Card><Statistic title="Veterinarians" value={report?.byVet?.length ?? 0} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* Monthly Trend */}
          <Col xs={24} lg={12}>
            <Card title="Cases & Cost Trend" size="small">
              {report?.monthlyTrend && report.monthlyTrend.length > 0 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <BarChart data={report.monthlyTrend}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="month" tick={{ fontSize: 10 }} />
                    <YAxis yAxisId="left" />
                    <YAxis yAxisId="right" orientation="right" />
                    <Tooltip />
                    <Legend />
                    <Bar yAxisId="left" dataKey="caseCount" name="Cases" fill="#1677ff" />
                    <Bar yAxisId="right" dataKey="cost" name="Cost" fill="#52c41a" />
                  </BarChart>
                </ResponsiveContainer>
              ) : <Empty description="No data" />}
            </Card>
          </Col>

          {/* Costs by Vet */}
          <Col xs={24} lg={12}>
            <Card title="Cost by Veterinarian" size="small">
              {vetData.length > 0 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <PieChart>
                    <Pie
                      data={vetData}
                      cx="50%" cy="50%" outerRadius={90}
                      label={({ name, percent }) => `${name} (${((percent ?? 0) * 100).toFixed(0)}%)`}
                      dataKey="value"
                    >
                      {vetData.map((_, i) => <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />)}
                    </Pie>
                    <Tooltip formatter={(value: number) => formatCurrency(value)} />
                  </PieChart>
                </ResponsiveContainer>
              ) : <Empty description="No data" />}
            </Card>
          </Col>
        </Row>

        {/* Status Breakdown */}
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={24} lg={12}>
            <Card title="By Status" size="small">
              {statusData.length > 0 ? (
                <ResponsiveContainer width="100%" height={200}>
                  <BarChart data={statusData} layout="vertical">
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis type="number" />
                    <YAxis type="category" dataKey="name" width={100} />
                    <Tooltip />
                    <Bar dataKey="value" name="Count" fill="#1677ff" />
                  </BarChart>
                </ResponsiveContainer>
              ) : <Empty description="No data" />}
            </Card>
          </Col>
        </Row>

        {/* Data Table */}
        <Card title="Costs by Veterinarian" size="small">
          <Table rowKey="vetName" columns={vetColumns} dataSource={report?.byVet ?? []} pagination={false} size="small" />
        </Card>
      </Spin>
    </div>
  );
};

export default MedicalReportsPage;
