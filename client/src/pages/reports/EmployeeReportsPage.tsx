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
import { reportsApi, type EmployeeReport, type EmployeePayrollByDepartment } from '../../api/reports';
import { getApiError } from '../../api/farmApi';
import { formatMoney } from '../../i18n/format';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

const formatCurrency = (v: number) =>
  formatMoney(v);

const EmployeeReportsPage: React.FC = () => {const { t } = useTranslation('reports'); 
  const [report, setReport] = useState<EmployeeReport | null>(null);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    try {
      const res = await reportsApi.employeeReport(from, to);
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

  const deptColumns: ColumnsType<EmployeePayrollByDepartment> = [
    { title: t('department'), dataIndex: 'departmentName' },
    { title: t('employees'), dataIndex: 'employeeCount', align: 'right' },
    { title: t('totalPaid'), dataIndex: 'totalPaid', align: 'right', render: (v: number) => formatCurrency(v) },
  ];

  const deptData = report?.byDepartment?.map(d => ({ name: d.departmentName, value: d.totalPaid })) ?? [];

  return (
    <div>
      <Card style={{ marginBottom: 16 }} extra={<div style={{ display: 'flex', gap: 8 }}><DateRangeFilter onChange={(from, to) => void loadData(from, to)} /><ExportButton filename="employee-report" title={t('employeeReport')} headers={['Department', 'Employees', 'Total Paid']} rows={(report?.byDepartment ?? []).map(d => [d.departmentName, d.employeeCount, d.totalPaid])} /></div>}>
        <Text type="secondary">{t('employeeListSalaryExpensesPaymentHistoryByDepartment')}</Text>
      </Card>

      <Spin spinning={loading}>
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalEmployees')} value={report?.totalEmployees ?? 0} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('active')} value={report?.activeEmployees ?? 0} valueStyle={{ color: '#52c41a' }} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalPaid')} value={report?.totalPaid ?? 0} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('payments')} value={report?.paymentCount ?? 0} /></Card></Col>
        </Row>

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {/* Monthly Payroll Bar Chart */}
          <Col xs={24} lg={14}>
            <Card title={t('payrollByMonth')} size="small">
              {report?.byMonth && report.byMonth.length > 0 ? (
                <ResponsiveContainer width="100%" height={280}>
                  <BarChart data={report.byMonth}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="month" tick={{ fontSize: 10 }} />
                    <YAxis />
                    <Tooltip formatter={(value) => formatCurrency(Number(value ?? 0))} />
                    <Legend />
                    <Bar dataKey="amount" name="Amount" fill="#1677ff" />
                  </BarChart>
                </ResponsiveContainer>
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>

          {/* Department Donut */}
          <Col xs={24} lg={10}>
            <Card title={t('costByDepartment')} size="small">
              {deptData.length > 0 ? (
                <BreakdownPieChart data={deptData} height={280} innerRadius={50} valueFormatter={formatCurrency} />
              ) : <Empty description={t('noData')} />}
            </Card>
          </Col>
        </Row>

        {/* Data Table */}
        <Card title={t('byDepartment')} size="small">
          <Table rowKey="departmentName" columns={deptColumns} dataSource={report?.byDepartment ?? []} pagination={false} size="small" />
        </Card>
      </Spin>
    </div>
  );
};

export default EmployeeReportsPage;
