import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, Row, Statistic, Table, Spin, Empty, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  BarChart, Bar,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import { reportsApi, type MedicalReport, type MedicalByVet } from '../../api/reports';
import { getApiError } from '../../api/farmApi';
import { formatMoney } from '../../i18n/format';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const formatCurrency = (v: number) =>
  formatMoney(v);

const MedicalReportsPage: React.FC = () => {const { t } = useTranslation('reports'); 
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
    { title: t('veterinarian'), dataIndex: 'vetName' },
    { title: t('cases'), dataIndex: 'caseCount', align: 'right' },
    { title: t('totalCost2'), dataIndex: 'totalCost', align: 'right', render: (v: number) => formatCurrency(v) },
  ];

  const statusData = report?.byStatus?.map(s => ({ name: s.status, value: s.count })) ?? [];
  const vetData = report?.byVet?.map(v => ({ name: v.vetName, value: v.totalCost })) ?? [];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="medical-report" title={t('medicalReport')} headers={['Vet', 'Cases', 'Cost']} rows={(report?.byVet ?? []).map(v => [v.vetName, v.caseCount, v.totalCost])} /></div>}>
        <Text type="secondary">{t('treatmentCasesCostsByVeterinarianMedicineUsage')}</Text>
      </Card>

      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={8}><Card><Statistic title={t('totalCases')} value={report?.totalCases ?? 0} /></Card></Col>
          <Col xs={12} sm={8}><Card><Statistic title={t('totalCost2')} value={report?.totalCost ?? 0} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={8}><Card><Statistic title={t('veterinarians')} value={report?.byVet?.length ?? 0} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* Monthly Trend */}
          <Col xs={24} lg={12}>
            <Card title={t('casesCostTrend')} size="small">
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
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>

          {/* Costs by Vet */}
          <Col xs={24} lg={12}>
            <Card title={t('costByVeterinarian')} size="small">
              {vetData.length > 0 ? (
                <BreakdownPieChart data={vetData} valueFormatter={formatCurrency} />
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>
        </Row>

        {/* Status Breakdown */}
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={24} lg={12}>
            <Card title={t('byStatus')} size="small">
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
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>
        </Row>

        {/* Data Table */}
        <Card title={t('costsByVeterinarian')} size="small">
          <Table rowKey="vetName" columns={vetColumns} dataSource={report?.byVet ?? []} pagination={false} size="small" />
        </Card>
      </Spin>
    </div>
  );
};

export default MedicalReportsPage;
