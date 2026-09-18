import React, { useState } from 'react';
import {
  Card, DatePicker, Row, Col, Statistic, Select, Table, Tabs, Tag,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import BreakdownPieChart from '../../components/BreakdownPieChart';
import { feedReportsApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import { message } from 'antd';
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

  // The first column of the wide tables is pinned so the row identity stays visible while the
  // rest scrolls sideways on a phone. Pinning needs `scroll.x`, which App.tsx sets globally.
  // NB: antd v6 only pins on `fixed: 'start'` — the legacy 'left' still type-checks but is
  // ignored (rc-table's isFixedStart compares strictly against 'start').
  const typeCols: ColumnsType<FeedTypeBreakdown> = [
    { title: 'Feed Type', dataIndex: 'feedTypeName', fixed: 'start', width: 140 },
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
    { title: 'Tag', dataIndex: 'tagNumber', fixed: 'start', width: 100 },
    { title: 'Name', dataIndex: 'name', render: (n?: string) => n ?? '-' },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right' },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
  ];

  const locationCols: ColumnsType<LocationConsumption> = [
    { title: 'Location', dataIndex: 'locationName', fixed: 'start', width: 130 },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right' },
    { title: 'Cost', dataIndex: 'cost', align: 'right' },
  ];

  return (
    <div>
      <Card style={{ marginBottom: 16 }}>
        {/* Row/Col rather than a Space: a Space never wraps, so on a phone the period select
            was pushed past the right edge and got clipped. */}
        <Row gutter={[8, 8]}>
          <Col xs={24} sm={14} md={10} lg={8}>
            <DatePicker.RangePicker
              value={range}
              onChange={(v) => v && setRange(v)}
              style={{ width: '100%' }}
            />
          </Col>
          <Col xs={24} sm={10} md={6} lg={4}>
            <Select
              value={period}
              onChange={setPeriod}
              options={[
                { value: 'day', label: 'Daily' },
                { value: 'week', label: 'Weekly' },
                { value: 'month', label: 'Monthly' },
              ]}
              style={{ width: '100%' }}
            />
          </Col>
        </Row>
      </Card>

      {/* Two cards per row on phones, three on small tablets, all six on desktop. The
          two-value gutter keeps a gap between the cards once they wrap. */}
      {summary && (
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Consumed" value={summary.totalConsumedQuantity} precision={2} suffix="kg" /></Card></Col>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Consumed Cost" value={summary.totalConsumedCost} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Purchased" value={summary.totalPurchasedQuantity} precision={2} suffix="kg" /></Card></Col>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Purchased Cost" value={summary.totalPurchasedCost} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Inventory Value" value={summary.currentInventoryValue} precision={2} prefix="$" /></Card></Col>
          <Col xs={12} sm={8} lg={4}><Card><Statistic title="Period" value={`${dayjs(summary.from).format('MMM D')} – ${dayjs(summary.to).format('MMM D')}`} /></Card></Col>
        </Row>
      )}

      <Card>
        <Tabs
          onChange={fetchTab}
          items={[
            {
              key: 'trend',
              // Short label: five full-length names cannot fit a phone-width tab bar.
              label: 'Trend',
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
              label: 'By Type',
              children: (
                <>
                  {byType.length > 0 ? (
                    /* The table below already lists every feed type with its share, so no legend here. */
                    <BreakdownPieChart
                      data={byType.map(i => ({ name: i.feedTypeName, value: i.cost }))}
                      valueFormatter={(v) => `$${v.toFixed(2)}`}
                      showLegend={false}
                    />
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
                  <Col xs={12} md={6}><Card><Statistic title="Consumed Qty" value={summary.totalConsumedQuantity} precision={2} /></Card></Col>
                  <Col xs={12} md={6}><Card><Statistic title="Consumed Cost" value={summary.totalConsumedCost} precision={2} prefix="$" /></Card></Col>
                  <Col xs={12} md={6}><Card><Statistic title="Purchased Qty" value={summary.totalPurchasedQuantity} precision={2} /></Card></Col>
                  <Col xs={12} md={6}><Card><Statistic title="Purchased Cost" value={summary.totalPurchasedCost} precision={2} prefix="$" /></Card></Col>
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
