import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert, Card, Col, Descriptions, Empty, Row, Spin, Statistic, Table, Tag, Tooltip, Typography, message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { InfoCircleOutlined } from '@ant-design/icons';
import DateRangeFilter from '../../components/DateRangeFilter';
import ExportButton from '../../components/ExportButton';
import {
  reportsApi,
  type AnimalCostRow, type CostComponent, type CostPerAnimalReport, type HerdCostRow,
} from '../../api/reports';
import { getApiError } from '../../api/farmApi';

const { Text } = Typography;

/**
 * Money is nullable on the wire: several optional figures (a caveat's amount, an allocation
 * pool on a direct cost) arrive as an explicit null. Formatting one used to throw inside
 * render and take the whole app down with it, so this is the one place that decides what an
 * absent amount looks like.
 */
const money = (value?: number | null): string =>
  value == null
    ? '—'
    : value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const day = (value?: string | null): string => (value ? new Date(value).toLocaleDateString() : 'Still here');

/**
 * The arithmetic behind one figure, in the words the report itself uses: a direct record is
 * its own number, and a share of a pool is shown as `days ÷ pool-days × pool` so the reader
 * can check it by hand instead of trusting a result.
 */
const allocationText = (component: CostComponent): string => {
  // `== null` on purpose: a direct cost serialises its pool fields as null, and so does any
  // component the API could not put a pool behind.
  if (component.method === 'direct' || component.poolAmount == null) {
    return component.source;
  }

  return `${component.source} · ${component.allocatedDays ?? 0} / ${component.poolDays ?? 0} days × ${money(component.poolAmount)}`;
};

const CostPerAnimalPage: React.FC = () => {
  const [report, setReport] = useState<CostPerAnimalReport | null>(null);
  const [loading, setLoading] = useState(false);

  const loadData = useCallback(async (from?: string, to?: string) => {
    setLoading(true);
    try {
      const res = await reportsApi.costPerAnimalReport(from, to);
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

  const componentColumns: ColumnsType<CostComponent> = [
    { title: 'Item', dataIndex: 'label' },
    { title: 'Amount', dataIndex: 'amount', align: 'right', render: (value: number) => money(value) },
    { title: 'How it was worked out', key: 'basis', render: (_, component) => (
      <Text type="secondary" style={{ fontSize: 12 }}>{allocationText(component)}</Text>
    ) },
    { title: 'Records', dataIndex: 'recordCount', align: 'right', render: (value: number, component) =>
      component.method === 'direct' && value ? value : <Text type="secondary">—</Text> },
  ];

  const animalColumns: ColumnsType<AnimalCostRow> = [
    { title: 'Tag', dataIndex: 'tagNumber' },
    { title: 'Name', dataIndex: 'name', render: (value?: string) => value ?? <Text type="secondary">—</Text> },
    { title: 'Herd', dataIndex: 'locationName', render: (value?: string) => value ?? <Text type="secondary">Not in a location</Text> },
    { title: 'Present', key: 'present', render: (_, row) => (
      <Text style={{ fontSize: 12 }}>
        {new Date(row.presentFrom).toLocaleDateString()} – {day(row.presentTo)}
      </Text>
    ) },
    { title: 'Animal-days', dataIndex: 'animalDays', align: 'right' },
    { title: 'Share of farm days', dataIndex: 'shareOfFarmDays', align: 'right',
      render: (value: number) => `${(value * 100).toFixed(1)}%` },
    { title: 'Cost', dataIndex: 'totalCost', align: 'right', render: (value: number) => money(value) },
    { title: 'Revenue', dataIndex: 'totalRevenue', align: 'right', render: (value: number) => money(value) },
    { title: 'Margin', dataIndex: 'margin', align: 'right', render: (value: number) => (
      <Text type={value < 0 ? 'danger' : undefined}>{money(value)}</Text>
    ) },
    { title: '', key: 'warnings', width: 90, render: (_, row) => row.warnings.length > 0 ? (
      <Tooltip title={row.warnings.map(code => warningText(code)).join(' ')}>
        <Tag icon={<InfoCircleOutlined />} color="warning">Caveat</Tag>
      </Tooltip>
    ) : null },
  ];

  const herdColumns: ColumnsType<HerdCostRow> = [
    { title: 'Herd', dataIndex: 'locationName' },
    { title: 'Animals', dataIndex: 'animalCount', align: 'right' },
    { title: 'Animal-days', dataIndex: 'animalDays', align: 'right' },
    { title: 'Cost', dataIndex: 'totalCost', align: 'right', render: (value: number) => money(value) },
    { title: 'Revenue', dataIndex: 'totalRevenue', align: 'right', render: (value: number) => money(value) },
    { title: 'Margin', dataIndex: 'margin', align: 'right', render: (value: number) => (
      <Text type={value < 0 ? 'danger' : undefined}>{money(value)}</Text>
    ) },
  ];

  const reconciliation = report?.reconciliation;

  return (
    <div>
      <Card
        style={{ marginBottom: 16 }}
        extra={
          <div style={{ display: 'flex', gap: 8 }}>
            <DateRangeFilter onChange={(from, to) => void loadData(from, to)} />
            <ExportButton
              filename="cost-per-animal"
              title="Cost per Animal"
              headers={['Tag', 'Name', 'Herd', 'Animal-days', 'Cost', 'Revenue', 'Margin']}
              rows={(report?.animals ?? []).map(row => [
                row.tagNumber, row.name ?? '', row.locationName ?? 'Not in a location',
                row.animalDays, row.totalCost, row.totalRevenue, row.margin,
              ])}
              disabled={!report}
            />
          </div>
        }
      >
        <Text type="secondary">
          What each animal cost and earned in the range. Shared costs are split by animal-days,
          and every allocated figure shows the days and the pool it came from.
        </Text>
      </Card>

      <Spin spinning={loading}>
        {/* The report's caveats come first: a farm missing feed records must not read this as a
            precise answer, so the warning is above the numbers rather than under them. */}
        {report && report.warnings.length > 0 && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            title="Read these numbers with the following in mind"
            description={
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {report.warnings.map(warning => (
                  <li key={warning.code}>
                    {warning.message}
                    {warning.amount != null && <> <Text strong>({money(warning.amount)})</Text></>}
                    {warning.affectedCount > 0 && <> <Text type="secondary">({warning.affectedCount} records/animals)</Text></>}
                  </li>
                ))}
              </ul>
            }
          />
        )}

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title="Total cost" value={report?.farm.totalCost ?? 0} precision={2} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title="Total revenue" value={report?.farm.totalRevenue ?? 0} precision={2} /></Card></Col>
          <Col xs={12} sm={6}><Card>
            <Statistic
              title="Margin"
              value={report?.farm.margin ?? 0}
              precision={2}
              styles={{ content: { color: (report?.farm.margin ?? 0) < 0 ? '#ff4d4f' : undefined } }}
            />
          </Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title="Cost per animal-day" value={report?.farm.costPerAnimalDay ?? 0} precision={2} /></Card></Col>
        </Row>

        <Card title="Cost and revenue per animal" size="small" style={{ marginBottom: 16 }}>
          <Table
            rowKey="animalId"
            size="small"
            columns={animalColumns}
            dataSource={report?.animals ?? []}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            locale={{ emptyText: <Empty description="No animals in this range" /> }}
            expandable={{
              expandedRowRender: (row) => (
                <Row gutter={[16, 16]}>
                  <Col xs={24} lg={12}>
                    <Text strong>Cost</Text>
                    <Table
                      rowKey="key"
                      size="small"
                      pagination={false}
                      columns={componentColumns}
                      dataSource={row.costs}
                      locale={{ emptyText: 'No cost recorded for this animal' }}
                    />
                  </Col>
                  <Col xs={24} lg={12}>
                    <Text strong>Revenue</Text>
                    <Table
                      rowKey="key"
                      size="small"
                      pagination={false}
                      columns={componentColumns}
                      dataSource={row.revenue}
                      locale={{ emptyText: 'No revenue recorded for this animal' }}
                    />
                  </Col>
                </Row>
              ),
            }}
          />
        </Card>

        <Card title="Cost and revenue per herd" size="small" style={{ marginBottom: 16 }}>
          <Table
            rowKey={(row) => row.locationId ?? 'none'}
            size="small"
            columns={herdColumns}
            dataSource={report?.herds ?? []}
            pagination={false}
            locale={{ emptyText: <Empty description="No data" /> }}
          />
          <Text type="secondary" style={{ fontSize: 12 }}>
            Herds group animals by where they are now, and each row is the sum of its animals' rows.
          </Text>
        </Card>

        <Row gutter={[16, 16]}>
          <Col xs={24} lg={12}>
            <Card title="How these numbers were built" size="small">
              {report?.rules.map(rule => (
                <p key={rule.key} style={{ marginBottom: 12 }}>
                  <Text strong>{rule.title}</Text>
                  <br />
                  <Text type="secondary">{rule.description}</Text>
                </p>
              ))}
            </Card>
          </Col>
          <Col xs={24} lg={12}>
            <Card title="Reconciliation" size="small">
              {!reconciliation ? <Empty description="No data" /> : (
                <>
                  <Descriptions size="small" column={1} bordered>
                    <Descriptions.Item label="Expenses in the range">
                      {money(reconciliation.farmExpensesTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label="…of which health-linked">
                      {money(reconciliation.healthLinkedExpenses)}
                    </Descriptions.Item>
                    <Descriptions.Item label="…on an animal">
                      {money(reconciliation.expensesAttributedToAnimals + reconciliation.expensesAllocatedFromLocations + reconciliation.expensesAllocatedFromFarmPool)}
                    </Descriptions.Item>
                    <Descriptions.Item label="…unallocated">
                      {money(reconciliation.expensesUnallocated)}
                    </Descriptions.Item>
                    <Descriptions.Item label="Feed consumed">
                      {money(reconciliation.feedConsumedTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label="Health records">
                      {money(reconciliation.healthRecordsTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label="Labour">
                      {money(reconciliation.labourTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label="Cost with no expense behind it">
                      {money(reconciliation.costOutsideTheExpenseLedger)}
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {' '}— feed consumption and labour, which the profit-and-loss expense line does not carry
                      </Text>
                    </Descriptions.Item>
                  </Descriptions>
                  <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                    <Tag color={reconciliation.expensesReconcile ? 'green' : 'red'}>
                      Expenses {reconciliation.expensesReconcile ? 'add up' : 'do not add up'}
                    </Tag>
                    <Tag color={reconciliation.feedReconciles ? 'green' : 'red'}>
                      Feed {reconciliation.feedReconciles ? 'adds up' : 'does not add up'}
                    </Tag>
                    <Tag color={reconciliation.healthReconciles ? 'green' : 'red'}>
                      Health {reconciliation.healthReconciles ? 'adds up' : 'does not add up'}
                    </Tag>
                  </div>
                </>
              )}
            </Card>
          </Col>
        </Row>
      </Spin>
    </div>
  );
};

/** Plain-language wording for the per-row caveat codes, so a tag is never a bare code. */
function warningText(code: string): string {
  switch (code) {
    case 'animal.presence-start-unknown':
      return 'No acquisition date or date of birth, so it is counted as present for the whole range.';
    case 'animal.departure-unknown':
      return 'Its status says it left, but no departure date could be read, so it is counted as present for the whole range.';
    default:
      return code;
  }
}

export default CostPerAnimalPage;
