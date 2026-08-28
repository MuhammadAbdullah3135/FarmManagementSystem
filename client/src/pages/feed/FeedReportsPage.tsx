import React, { useState } from 'react';
import {
  Card, DatePicker, Row, Col, Statistic, Select, Space, Table, Tabs, Tag,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { PieChart, Pie, Cell, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { feedReportsApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import { message } from 'antd';

const PIE_COLORS = ['#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2'];
import type {
  ConsumptionTrendPoint,
  FeedTypeBreakdown,
  AnimalConsumption,
  LocationConsumption,
  FeedCostSummary,
} from '../../types';

const FeedReportsPage: React.FC = () => {
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null]>([dayjs().subtract(30, 'day'), dayjs()]);
  const [period, setPeriod] = useState<'day' | 'week' | 'month'>('day');
  const [trend, setTrend] = useState<ConsumptionTrendPoint[]>([]);
  const [byType, setByType] = useState<FeedTypeBreakdown[]>([]);
  const [byAnimal, setByAnimal] = useState<AnimalConsumption[]>([]);
  const [byLocation, setByLocation] = useState<LocationConsumption[]>([]);
  const [summary, setSummary] = useState<FeedCostSummary | null>(null);
  const [loading, setLoading] = useState(false);

  const from = range[0]?.toISOString();
  const to = range[1]?.toISOString();

  const fetchTab = async (key: string) => {
    setLoading(true);
    try {
      if (key === 'trend') {
        const r = await feedReportsApi.consumptionTrend(period, from, to);
        setTrend(r.data);
      } else if (key === 'byType') {
        const r = await feedReportsApi.byFeedType(from, to);
        setByType(r.data);
      } else if (key === 'byAnimal') {
        const r = await feedReportsApi.byAnimal(from, to);
        setByAnimal(r.data);
      } else if (key === 'byLocation') {
        const r = await feedReportsApi.byLocation(from, to);
        setByLocation(r.data);
      } else if (key === 'cost') {
        const r = await feedReportsApi.costSummary(from, to);
        setSummary(r.data);
      }
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  };

  const trendCols: ColumnsType<ConsumptionTrendPoint> = [
    { title: 'Period', dataIndex: 'label' },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right' },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
  ];

  const typeCols: ColumnsType<FeedTypeBreakdown> = [
    { title: 'Feed Type', dataIndex: 'feedTypeName' },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right', render: (v, r) => `${v} ${r.unitName}` },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
    {
      title: 'Share',
      dataIndex: 'sharePercent',
      align: 'right',
      render: (v: number) => <Tag color="blue">{v.toFixed(1)}%</Tag>,
    },
  ];

  const animalCols: ColumnsType<AnimalConsumption> = [
    { title: 'Tag', dataIndex: 'tagNumber' },
    { title: 'Name', dataIndex: 'name', render: (n?: string) => n ?? '-' },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right' },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
  ];

  const locationCols: ColumnsType<LocationConsumption> = [
    { title: 'Location', dataIndex: 'locationName' },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right' },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
  ];

  return (
    <div>
      <Card style={{ marginBottom: 16 }}>
        <Space>
          <DatePicker.RangePicker value={range} onChange={(v) => v && setRange(v)} />
          <Select
            value={period}
            onChange={setPeriod}
            options={[
              { value: 'day', label: 'Daily' },
              { value: 'week', label: 'Weekly' },
              { value: 'month', label: 'Monthly' },
            ]}
            style={{ width: 120 }}
          />
        </Space>
      </Card>

      {summary && (
        <Row gutter={16} style={{ marginBottom: 16 }}>
          <Col span={4}><Card><Statistic title="Consumed" value={summary.totalConsumedQuantity} precision={2} suffix="kg" /></Card></Col>
          <Col span={4}><Card><Statistic title="Consumed Cost" value={summary.totalConsumedCost} precision={2} prefix="$" /></Card></Col>
          <Col span={4}><Card><Statistic title="Purchased" value={summary.totalPurchasedQuantity} precision={2} suffix="kg" /></Card></Col>
          <Col span={4}><Card><Statistic title="Purchased Cost" value={summary.totalPurchasedCost} precision={2} prefix="$" /></Card></Col>
          <Col span={4}><Card><Statistic title="Inventory Value" value={summary.currentInventoryValue} precision={2} prefix="$" /></Card></Col>
          <Col span={4}><Card><Statistic title="Period" value={`${dayjs(summary.from).format('MMM D')} – ${dayjs(summary.to).format('MMM D')}`} /></Card></Col>
        </Row>
      )}

      <Card>
        <Tabs
          onChange={fetchTab}
          items={[
            {
              key: 'trend',
              label: 'Consumption Trend',
              children: (
                <>
                  {trend.length > 0 ? (
                    <ResponsiveContainer width="100%" height={260}>
                      <LineChart data={trend}>
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis dataKey="label" tick={{ fontSize: 10 }} />
                        <YAxis />
                        <Tooltip />
                        <Legend />
                        <Line type="monotone" dataKey="quantity" name="Quantity" stroke="#1677ff" dot={false} />
                        <Line type="monotone" dataKey="cost" name="Cost" stroke="#52c41a" dot={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  ) : null}
                  <Table rowKey="label" columns={trendCols} dataSource={trend} loading={loading} pagination={false} style={{ marginTop: 16 }} />
                </>
              ),
            },
            {
              key: 'byType',
              label: 'By Feed Type',
              children: (
                <>
                  {byType.length > 0 ? (
                    <ResponsiveContainer width="100%" height={260}>
                      <PieChart>
                        <Pie
                          data={byType.map(i => ({ name: i.feedTypeName, value: i.cost }))}
                          cx="50%" cy="50%" outerRadius={90}
                          label={({ name, percent }) => `${name} (${((percent ?? 0) * 100).toFixed(0)}%)`}
                          dataKey="value"
                        >
                          {byType.map((_, i) => <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />)}
                        </Pie>
                        <Tooltip formatter={(v) => `$${Number(v ?? 0).toFixed(2)}`} />
                      </PieChart>
                    </ResponsiveContainer>
                  ) : null}
                  <Table rowKey="feedTypeId" columns={typeCols} dataSource={byType} loading={loading} pagination={false} style={{ marginTop: 16 }} />
                </>
              ),
            },
            {
              key: 'byAnimal',
              label: 'By Animal',
              children: <Table rowKey="animalId" columns={animalCols} dataSource={byAnimal} loading={loading} pagination={false} />,
            },
            {
              key: 'byLocation',
              label: 'By Location',
              children: <Table rowKey="locationId" columns={locationCols} dataSource={byLocation} loading={loading} pagination={false} />,
            },
            {
              key: 'cost',
              label: 'Cost Summary',
              children: summary ? (
                <Row gutter={[16, 16]}>
                  <Col span={6}><Card><Statistic title="Consumed Qty" value={summary.totalConsumedQuantity} precision={2} /></Card></Col>
                  <Col span={6}><Card><Statistic title="Consumed Cost" value={summary.totalConsumedCost} precision={2} prefix="$" /></Card></Col>
                  <Col span={6}><Card><Statistic title="Purchased Qty" value={summary.totalPurchasedQuantity} precision={2} /></Card></Col>
                  <Col span={6}><Card><Statistic title="Purchased Cost" value={summary.totalPurchasedCost} precision={2} prefix="$" /></Card></Col>
                </Row>
              ) : loading ? <Table loading columns={[]} dataSource={[]} pagination={false} /> : <p>Select a date range and click this tab to load.</p>,
            },
          ]}
        />
      </Card>
    </div>
  );
};

export default FeedReportsPage;
