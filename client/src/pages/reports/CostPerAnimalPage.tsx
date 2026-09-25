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
import { formatDate, formatNumber, formatPercent } from '../../i18n/format';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

/**
 * Money is nullable on the wire: several optional figures (a caveat's amount, an allocation
 * pool on a direct cost) arrive as an explicit null. Formatting one used to throw inside
 * render and take the whole app down with it, so this is the one place that decides what an
 * absent amount looks like.
 *
 * The figure stays bare — no currency symbol — because that is what this report has always
 * shown and what its assertions pin: a column header already says the number is money.
 * `formatNumber` is what still gives the language its own separators.
 */
const money = (value?: number | null): string =>
  value == null
    ? '—'
    : formatNumber(value, undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** An open-ended range: no end date yet means the animal is still on the farm. */
const day = (value: string | null | undefined, openLabel: string): string =>
  value ? formatDate(value) : openLabel;

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

const CostPerAnimalPage: React.FC = () => {const { t } = useTranslation('reports'); 
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
    { title: t('item'), dataIndex: 'label' },
    { title: t('amount'), dataIndex: 'amount', align: 'right', render: (value: number) => money(value) },
    { title: t('howItWasWorkedOut'), key: 'basis', render: (_, component) => (
      <Text type="secondary" style={{ fontSize: 12 }}>{allocationText(component)}</Text>
    ) },
    { title: t('records'), dataIndex: 'recordCount', align: 'right', render: (value: number, component) =>
      component.method === 'direct' && value ? value : <Text type="secondary">—</Text> },
  ];

  const animalColumns: ColumnsType<AnimalCostRow> = [
    { title: t('tag'), dataIndex: 'tagNumber' },
    { title: t('name'), dataIndex: 'name', render: (value?: string) => value ?? <Text type="secondary">—</Text> },
    { title: t('herd'), dataIndex: 'locationName', render: (value?: string) => value ?? <Text type="secondary">{t('notInALocation')}</Text> },
    { title: t('present'), key: 'present', render: (_, row) => (
      <Text style={{ fontSize: 12 }}>
        {formatDate(row.presentFrom)} – {day(row.presentTo, t('stillHere'))}
      </Text>
    ) },
    { title: t('animalDays'), dataIndex: 'animalDays', align: 'right' },
    { title: t('shareOfFarmDays'), dataIndex: 'shareOfFarmDays', align: 'right',
      render: (value: number) => formatPercent(value) },
    { title: t('cost'), dataIndex: 'totalCost', align: 'right', render: (value: number) => money(value) },
    { title: t('revenue'), dataIndex: 'totalRevenue', align: 'right', render: (value: number) => money(value) },
    { title: t('margin'), dataIndex: 'margin', align: 'right', render: (value: number) => (
      <Text type={value < 0 ? 'danger' : undefined}>{money(value)}</Text>
    ) },
    { title: '', key: 'warnings', width: 90, render: (_, row) => row.warnings.length > 0 ? (
      <Tooltip title={row.warnings.map(code => warningText(code)).join(' ')}>
        <Tag icon={<InfoCircleOutlined />} color="warning">{t('caveat')}</Tag>
      </Tooltip>
    ) : null },
  ];

  const herdColumns: ColumnsType<HerdCostRow> = [
    { title: t('herd'), dataIndex: 'locationName' },
    { title: t('animals'), dataIndex: 'animalCount', align: 'right' },
    { title: t('animalDays'), dataIndex: 'animalDays', align: 'right' },
    { title: t('cost'), dataIndex: 'totalCost', align: 'right', render: (value: number) => money(value) },
    { title: t('revenue'), dataIndex: 'totalRevenue', align: 'right', render: (value: number) => money(value) },
    { title: t('margin'), dataIndex: 'margin', align: 'right', render: (value: number) => (
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
              title={t('costPerAnimal')}
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
          {t('whatEachAnimalCostAndEarnedInThe')}
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
            title={t('readTheseNumbersWithTheFollowingInMind')}
            description={
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {report.warnings.map(warning => (
                  <li key={warning.code}>
                    {warning.message}
                    {warning.amount != null && <> <Text strong>({money(warning.amount)})</Text></>}
                    {warning.affectedCount > 0 && <> <Text type="secondary">({warning.affectedCount} {t('recordsAnimals')}</Text></>}
                  </li>
                ))}
              </ul>
            }
          />
        )}

        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalCost')} value={report?.farm.totalCost ?? 0} precision={2} /></Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('totalRevenue')} value={report?.farm.totalRevenue ?? 0} precision={2} /></Card></Col>
          <Col xs={12} sm={6}><Card>
            <Statistic
              title={t('margin')}
              value={report?.farm.margin ?? 0}
              precision={2}
              styles={{ content: { color: (report?.farm.margin ?? 0) < 0 ? '#ff4d4f' : undefined } }}
            />
          </Card></Col>
          <Col xs={12} sm={6}><Card><Statistic title={t('costPerAnimalDay')} value={report?.farm.costPerAnimalDay ?? 0} precision={2} /></Card></Col>
        </Row>

        <Card title={t('costAndRevenuePerAnimal')} size="small" style={{ marginBottom: 16 }}>
          <Table
            rowKey="animalId"
            size="small"
            columns={animalColumns}
            dataSource={report?.animals ?? []}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            locale={{ emptyText: <Empty description={t('noAnimalsInThisRange')} /> }}
            expandable={{
              expandedRowRender: (row) => (
                <Row gutter={[16, 16]}>
                  <Col xs={24} lg={12}>
                    <Text strong>{t('cost')}</Text>
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
                    <Text strong>{t('revenue')}</Text>
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

        <Card title={t('costAndRevenuePerHerd')} size="small" style={{ marginBottom: 16 }}>
          <Table
            rowKey={(row) => row.locationId ?? 'none'}
            size="small"
            columns={herdColumns}
            dataSource={report?.herds ?? []}
            pagination={false}
            locale={{ emptyText: <Empty description={t('noData')} /> }}
          />
          <Text type="secondary" style={{ fontSize: 12 }}>
            {t('herdsGroupAnimalsByWhereTheyAreNow')}
          </Text>
        </Card>

        <Row gutter={[16, 16]}>
          <Col xs={24} lg={12}>
            <Card title={t('howTheseNumbersWereBuilt')} size="small">
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
            <Card title={t('reconciliation')} size="small">
              {!reconciliation ? <Empty description={t('noData')} /> : (
                <>
                  <Descriptions size="small" column={1} bordered>
                    <Descriptions.Item label={t('expensesInTheRange')}>
                      {money(reconciliation.farmExpensesTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('ofWhichHealthLinked')}>
                      {money(reconciliation.healthLinkedExpenses)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('onAnAnimal')}>
                      {money(reconciliation.expensesAttributedToAnimals + reconciliation.expensesAllocatedFromLocations + reconciliation.expensesAllocatedFromFarmPool)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('unallocated')}>
                      {money(reconciliation.expensesUnallocated)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('feedConsumed')}>
                      {money(reconciliation.feedConsumedTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('healthRecords')}>
                      {money(reconciliation.healthRecordsTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('labour')}>
                      {money(reconciliation.labourTotal)}
                    </Descriptions.Item>
                    <Descriptions.Item label={t('costWithNoExpenseBehindIt')}>
                      {money(reconciliation.costOutsideTheExpenseLedger)}
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {' '}{t('feedConsumptionAndLabourWhichTheProfitAnd')}
                      </Text>
                    </Descriptions.Item>
                  </Descriptions>
                  <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                    <Tag color={reconciliation.expensesReconcile ? 'green' : 'red'}>
                      {t('expenses')} {reconciliation.expensesReconcile ? t('addUp') : t('doNotAddUp')}
                    </Tag>
                    <Tag color={reconciliation.feedReconciles ? 'green' : 'red'}>
                      {t('feed')} {reconciliation.feedReconciles ? t('addsUp') : t('doesNotAddUp')}
                    </Tag>
                    <Tag color={reconciliation.healthReconciles ? 'green' : 'red'}>
                      {t('health')} {reconciliation.healthReconciles ? t('addsUp') : t('doesNotAddUp')}
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
