import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Table, Tag, Spin, Empty, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  BarChart, Bar,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import { reportsApi, type VaccinationReport, type VaccinationByVaccine } from '../../api/reports';
import { vaccinationStatusApi } from '../../api/health';
import type { VaccinationStatus } from '../../types';
import { getApiError } from '../../api/farmApi';
import { formatMoney, formatDate } from '../../i18n/format';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const formatCurrency = (v: number) =>
  formatMoney(v);

const VaccinationReportsPage: React.FC = () => {const { t } = useTranslation('reports'); 
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
    { title: t('vaccine'), dataIndex: 'vaccineName' },
    { title: t('count'), dataIndex: 'count', align: 'right' },
    { title: t('totalCost2'), dataIndex: 'totalCost', align: 'right', render: (v: number) => formatCurrency(v) },
  ];

  const vaccineData = report?.byVaccine?.map(v => ({ name: v.vaccineName, value: v.count })) ?? [];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="vaccination-report" title={t('vaccinationReport')} headers={['Vaccine', 'Count', 'Cost']} rows={(report?.byVaccine ?? []).map(v => [v.vaccineName, v.count, v.totalCost])} /></div>}>
        <Text type="secondary">{t('vaccinationHistoryUpcomingSchedulesOverdueAlerts')}</Text>
      </Card>

      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalVaccinations')} value={report?.totalVaccinations ?? 0} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalCost2')} value={report?.totalCost ?? 0} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('overdue')} value={overdue.length} valueStyle={{ color: overdue.length ? '#ff4d4f' : undefined }} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('upcomingDue')} value={upcoming.length} valueStyle={{ color: upcoming.length ? '#faad14' : undefined }} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* Monthly Trend */}
          <Col xs={24} lg={12}>
            <Card title={t('vaccinationTrend')} size="small">
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
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>

          {/* By Vaccine Donut */}
          <Col xs={24} lg={12}>
            <Card title={t('byVaccineType')} size="small">
              {vaccineData.length > 0 ? (
                <BreakdownPieChart data={vaccineData} innerRadius={50} outerRadius={90} />
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>
        </Row>

        {/* Overdue Table */}
        {overdue.length > 0 && (
          <Card title={t('overdueVaccinations')} size="small" style={{ marginBottom: 16 }}>
            <Table
              rowKey={(r) => `${r.animalId}-${r.vaccineTypeId}`}
              columns={[
                { title: 'Animal', key: 'animal', render: (_, r) => <><strong>{r.animalTagNumber}</strong>{r.animalName && <Text type="secondary"> ({r.animalName})</Text>}</> },
                { title: 'Vaccine', dataIndex: 'vaccineTypeName' },
                { title: t('nextDue'), dataIndex: 'nextDueDate', render: (v: string) => formatDate(v) },
                { title: t('daysOverdue'), dataIndex: 'daysUntilDue', render: (v: number) => <Tag color="red">{Math.abs(v)} {t('days')}</Tag> },
              ]}
              dataSource={overdue}
              pagination={false}
              size="small"
            />
          </Card>
        )}

        {/* Data Table */}
        <Card title={t('vaccinationsByType')} size="small">
          <Table rowKey="vaccineName" columns={vaccineColumns} dataSource={report?.byVaccine ?? []} pagination={false} size="small" />
        </Card>
      </Spin>
    </div>
  );
};

export default VaccinationReportsPage;
