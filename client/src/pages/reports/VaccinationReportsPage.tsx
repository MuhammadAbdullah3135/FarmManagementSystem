import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Table, Tag, Spin, Empty, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  BarChart, Bar, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import { reportsApi, type VaccinationReport, type VaccinationByVaccine } from '../../api/reports';
import { vaccinationStatusApi, type VaccinationStatus } from '../../api/health';
import { getApiError } from '../../api/farmApi';

const { Text } = Typography;
const PIE_COLORS = ['#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2'];

const formatCurrency = (v: number) =>
  `$${v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const VaccinationReportsPage: React.FC = () => {
  const [report, setReport] = useState<VaccinationReport | null>(null);
  const [vaccinationStatus, setVaccinationStatus] = useState<VaccinationStatus[]>([]);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    try {
      const [reportRes, statusRes] = await Promise.all([
        reportsApi.vaccinationReport(from, to),
        vaccinationStatusApi.all(),
      ]);
      setReport(reportRes.data);
      setVaccinationStatus(statusRes.data);
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

  const overdue = vaccinationStatus.filter(v => v.status === 'Overdue');
  const upcoming = vaccinationStatus.filter(v => v.status === 'Upcoming' || v.status === 'Due');

  const vaccineColumns: ColumnsType<VaccinationByVaccine> = [
    { title: 'Vaccine', dataIndex: 'vaccineName' },
    { title: 'Count', dataIndex: 'count', align: 'right' },
    { title: 'Total Cost', dataIndex: 'totalCost', align: 'right', render: (v: number) => formatCurrency(v) },
  ];

  const vaccineData = report?.byVaccine?.map(v => ({ name: v.vaccineName, value: v.count })) ?? [];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="vaccination-report" title="Vaccination Report" headers={['Vaccine', 'Count', 'Cost']} rows={(report?.byVaccine ?? []).map(v => [v.vaccineName, v.count, v.totalCost])} /></div>}>
        <Text type="secondary">Vaccination history, upcoming schedules, overdue alerts</Text>
      </Card>

      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title="Total Vaccinations" value={report?.totalVaccinations ?? 0} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title="Total Cost" value={report?.totalCost ?? 0} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title="Overdue" value={overdue.length} valueStyle={{ color: overdue.length ? '#ff4d4f' : undefined }} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title="Upcoming/Due" value={upcoming.length} valueStyle={{ color: upcoming.length ? '#faad14' : undefined }} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* Monthly Trend */}
          <Col xs={24} lg={12}>
            <Card title="Vaccination Trend" size="small">
              {report?.monthlyTrend && report.monthlyTrend.length > 0 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <BarChart data={report.monthlyTrend}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="month" tick={{ fontSize: 10 }} />
                    <YAxis />
                    <Tooltip />
                    <Legend />
                    <Bar dataKey="count" name="Count" fill="#1677ff" />
                    <Bar dataKey="cost" name="Cost" fill="#52c41a" />
                  </BarChart>
                </ResponsiveContainer>
              ) : <Empty description="No data" />}
            </Card>
          </Col>

          {/* By Vaccine Donut */}
          <Col xs={24} lg={12}>
            <Card title="By Vaccine Type" size="small">
              {vaccineData.length > 0 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <PieChart>
                    <Pie
                      data={vaccineData}
                      cx="50%" cy="50%" innerRadius={50} outerRadius={90}
                      label={({ name, percent }) => `${name} (${((percent ?? 0) * 100).toFixed(0)}%)`}
                      dataKey="value"
                    >
                      {vaccineData.map((_, i) => <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />)}
                    </Pie>
                    <Tooltip />
                  </PieChart>
                </ResponsiveContainer>
              ) : <Empty description="No data" />}
            </Card>
          </Col>
        </Row>

        {/* Overdue Table */}
        {overdue.length > 0 && (
          <Card title="Overdue Vaccinations" size="small" style={{ marginBottom: 16 }}>
            <Table
              rowKey={(r) => `${r.animalId}-${r.vaccineTypeId}`}
              columns={[
                { title: 'Animal', key: 'animal', render: (_, r) => <><strong>{r.animalTagNumber}</strong>{r.animalName && <Text type="secondary"> ({r.animalName})</Text>}</> },
                { title: 'Vaccine', dataIndex: 'vaccineTypeName' },
                { title: 'Next Due', dataIndex: 'nextDueDate', render: (v: string) => new Date(v).toLocaleDateString() },
                { title: 'Days Overdue', dataIndex: 'daysUntilDue', render: (v: number) => <Tag color="red">{Math.abs(v)} days</Tag> },
              ]}
              dataSource={overdue}
              pagination={false}
              size="small"
            />
          </Card>
        )}

        {/* Data Table */}
        <Card title="Vaccinations by Type" size="small">
          <Table rowKey="vaccineName" columns={vaccineColumns} dataSource={report?.byVaccine ?? []} pagination={false} size="small" />
        </Card>
      </Spin>
    </div>
  );
};

export default VaccinationReportsPage;
