import React, { useCallback, useEffect, useState } from 'react';
import { Card, Col, DatePicker, Row, Select, Statistic, Table, message } from 'antd';
import { ArrowDownOutlined, ArrowUpOutlined, DollarOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import { financeReportsApi, type FinanceReportFilter } from '../../api/finance';
import { getApiError } from '../../api/farmApi';
import type { CategoryBreakdownItem, MonthlySummaryItem, ProfitLossReport } from '../../types';

const { RangePicker } = DatePicker;

const formatCurrency = (amount: number) =>
  `$${amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

const FinanceReportsPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [year, setYear] = useState<number>(new Date().getFullYear());

  const [plReport, setPlReport] = useState<ProfitLossReport | null>(null);
  const [expenseBreakdown, setExpenseBreakdown] = useState<CategoryBreakdownItem[]>([]);
  const [expenseGrandTotal, setExpenseGrandTotal] = useState(0);
  const [incomeBreakdown, setIncomeBreakdown] = useState<CategoryBreakdownItem[]>([]);
  const [incomeGrandTotal, setIncomeGrandTotal] = useState(0);
  const [monthlySummary, setMonthlySummary] = useState<MonthlySummaryItem[]>([]);

  const getFilter = useCallback((): FinanceReportFilter => {
    if (dateRange && dateRange[0] && dateRange[1]) {
      return {
        from: dateRange[0].format('YYYY-MM-DD'),
        to: dateRange[1].format('YYYY-MM-DD'),
      };
    }
    return {};
  }, [dateRange]);

  const loadReports = useCallback(async () => {
    setLoading(true);
    try {
      const filter = getFilter();
      const [plRes, expRes, incRes, monthRes] = await Promise.all([
        financeReportsApi.profitLoss(filter),
        financeReportsApi.expenseBreakdown(filter),
        financeReportsApi.incomeBreakdown(filter),
        financeReportsApi.monthlySummary(year),
      ]);
      setPlReport(plRes.data);
      setExpenseBreakdown(expRes.data.items);
      setExpenseGrandTotal(expRes.data.grandTotal);
      setIncomeBreakdown(incRes.data.items);
      setIncomeGrandTotal(incRes.data.grandTotal);
      setMonthlySummary(monthRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [getFilter, year]);

  useEffect(() => {
    const timer = window.setTimeout(() => { loadReports(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadReports]);

  const breakdownColumns: ColumnsType<CategoryBreakdownItem> = [
    { title: 'Category', dataIndex: 'categoryName' },
    { title: 'Amount', dataIndex: 'total', align: 'right', render: (v: number) => formatCurrency(v) },
    { title: '%', dataIndex: 'percentage', align: 'right', width: 80, render: (v: number) => `${v}%` },
  ];

  const monthlyColumns: ColumnsType<MonthlySummaryItem> = [
    { title: 'Month', dataIndex: 'monthName', width: 120 },
    { title: 'Income', dataIndex: 'income', align: 'right', render: (v: number) => <span style={{ color: '#52c41a' }}>{formatCurrency(v)}</span> },
    { title: 'Expenses', dataIndex: 'expenses', align: 'right', render: (v: number) => <span style={{ color: '#ff4d4f' }}>{formatCurrency(v)}</span> },
    { title: 'Net', dataIndex: 'net', align: 'right', render: (v: number) => (
      <span style={{ color: v >= 0 ? '#52c41a' : '#ff4d4f', fontWeight: 'bold' }}>{formatCurrency(v)}</span>
    )},
  ];

  const yearOptions = Array.from({ length: 5 }, (_, i) => {
    const y = new Date().getFullYear() - i;
    return { value: y, label: String(y) };
  });

  return (
    <div style={{ padding: 0 }}>
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col>
          <RangePicker
            value={dateRange as [Dayjs, Dayjs] | null}
            onChange={(dates) => setDateRange(dates as [Dayjs | null, Dayjs | null] | null)}
            allowClear
            placeholder={['From', 'To']}
          />
        </Col>
        <Col>
          <Select
            value={year}
            onChange={setYear}
            options={yearOptions}
            style={{ width: 100 }}
          />
        </Col>
      </Row>

      {/* P&L Summary Cards */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={8}>
          <Card loading={loading}>
            <Statistic
              title="Total Income"
              value={plReport?.totalIncome ?? 0}
              precision={2}
              prefix={<ArrowUpOutlined />}
              valueStyle={{ color: '#52c41a' }}
              formatter={(value) => formatCurrency(value as number)}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card loading={loading}>
            <Statistic
              title="Total Expenses"
              value={plReport?.totalExpenses ?? 0}
              precision={2}
              prefix={<ArrowDownOutlined />}
              valueStyle={{ color: '#ff4d4f' }}
              formatter={(value) => formatCurrency(value as number)}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card loading={loading}>
            <Statistic
              title="Net Profit"
              value={plReport?.netProfit ?? 0}
              precision={2}
              prefix={<DollarOutlined />}
              valueStyle={{ color: (plReport?.netProfit ?? 0) >= 0 ? '#52c41a' : '#ff4d4f' }}
              formatter={(value) => formatCurrency(value as number)}
            />
          </Card>
        </Col>
      </Row>

      {/* Breakdown Charts */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={12}>
          <Card title="Expense Breakdown" loading={loading}>
            {expenseBreakdown.length > 0 ? (
              /* The table below already lists every category with its share, so no legend here. */
              <BreakdownPieChart
                data={expenseBreakdown.map(i => ({ name: i.categoryName, value: i.total }))}
                valueFormatter={formatCurrency}
                showLegend={false}
              />
            ) : null}
            <Table
              rowKey="categoryId"
              columns={breakdownColumns}
              dataSource={expenseBreakdown}
              pagination={false}
              size="small"
              locale={{ emptyText: 'No expenses in this period' }}
              summary={() => expenseBreakdown.length > 0 ? (
                <Table.Summary.Row>
                  <Table.Summary.Cell index={0}><strong>Total</strong></Table.Summary.Cell>
                  <Table.Summary.Cell index={1} align="right"><strong>{formatCurrency(expenseGrandTotal)}</strong></Table.Summary.Cell>
                  <Table.Summary.Cell index={2} align="right"><strong>100%</strong></Table.Summary.Cell>
                </Table.Summary.Row>
              ) : null}
            />
          </Card>
        </Col>
        <Col span={12}>
          <Card title="Income Breakdown" loading={loading}>
            {incomeBreakdown.length > 0 ? (
              <BreakdownPieChart
                data={incomeBreakdown.map(i => ({ name: i.categoryName, value: i.total }))}
                valueFormatter={formatCurrency}
                showLegend={false}
              />
            ) : null}
            <Table
              rowKey="categoryId"
              columns={breakdownColumns}
              dataSource={incomeBreakdown}
              pagination={false}
              size="small"
              locale={{ emptyText: 'No income in this period' }}
              summary={() => incomeBreakdown.length > 0 ? (
                <Table.Summary.Row>
                  <Table.Summary.Cell index={0}><strong>Total</strong></Table.Summary.Cell>
                  <Table.Summary.Cell index={1} align="right"><strong>{formatCurrency(incomeGrandTotal)}</strong></Table.Summary.Cell>
                  <Table.Summary.Cell index={2} align="right"><strong>100%</strong></Table.Summary.Cell>
                </Table.Summary.Row>
              ) : null}
            />
          </Card>
        </Col>
      </Row>

      {/* Monthly P/L Chart */}
      {monthlySummary.length > 0 && (
        <Card title={`Monthly Profit & Loss — ${year}`} loading={loading} style={{ marginBottom: 24 }}>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={monthlySummary}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="monthName" />
              <YAxis />
              <Tooltip formatter={(value) => formatCurrency(Number(value ?? 0))} />
              <Legend />
              <Bar dataKey="income" name="Income" fill="#52c41a" />
              <Bar dataKey="expenses" name="Expenses" fill="#ff4d4f" />
            </BarChart>
          </ResponsiveContainer>
        </Card>
      )}

      {/* Monthly Summary Table */}
      <Card title={`Monthly Summary — ${year}`} loading={loading}>
        <Table
          rowKey="month"
          columns={monthlyColumns}
          dataSource={monthlySummary}
          pagination={false}
          size="small"
          summary={() => {
            const totalIncome = monthlySummary.reduce((s, m) => s + m.income, 0);
            const totalExpenses = monthlySummary.reduce((s, m) => s + m.expenses, 0);
            const totalNet = totalIncome - totalExpenses;
            return monthlySummary.length > 0 ? (
              <Table.Summary.Row>
                <Table.Summary.Cell index={0}><strong>Year Total</strong></Table.Summary.Cell>
                <Table.Summary.Cell index={1} align="right"><strong style={{ color: '#52c41a' }}>{formatCurrency(totalIncome)}</strong></Table.Summary.Cell>
                <Table.Summary.Cell index={2} align="right"><strong style={{ color: '#ff4d4f' }}>{formatCurrency(totalExpenses)}</strong></Table.Summary.Cell>
                <Table.Summary.Cell index={3} align="right"><strong style={{ color: totalNet >= 0 ? '#52c41a' : '#ff4d4f' }}>{formatCurrency(totalNet)}</strong></Table.Summary.Cell>
              </Table.Summary.Row>
            ) : null;
          }}
        />
      </Card>
    </div>
  );
};

export default FinanceReportsPage;
