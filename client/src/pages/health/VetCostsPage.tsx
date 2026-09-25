import React, { useCallback, useEffect, useState } from 'react';
import { formatMoney } from '../../i18n/format';
import { Card, Col, DatePicker, Row, Statistic, Table, Tag, message } from 'antd';
import { DollarOutlined, MedicineBoxOutlined, ExperimentOutlined, TeamOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { healthCostsApi } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import type { HealthCostSummary, HealthCostByVet, HealthCostByAnimal, HealthCostByMonth } from '../../types';
import { useTranslation } from 'react-i18next';

const { RangePicker } = DatePicker;

const VetCostsPage: React.FC = () => {const { t } = useTranslation('health'); 
  const [summary, setSummary] = useState<HealthCostSummary | null>(null);
  const [byVet, setByVet] = useState<HealthCostByVet[]>([]);
  const [byAnimal, setByAnimal] = useState<HealthCostByAnimal[]>([]);
  const [byMonth, setByMonth] = useState<HealthCostByMonth[]>([]);
  const [loading, setLoading] = useState(false);
  const [filter, setFilter] = useState<{ from?: string; to?: string }>({});

  const load = useCallback(async (f: { from?: string; to?: string }) => {
    setLoading(true);
    try {
      const [sumRes, vetRes, animalRes, monthRes] = await Promise.all([
        healthCostsApi.summary(f),
        healthCostsApi.byVet(f),
        healthCostsApi.byAnimal(f),
        healthCostsApi.byMonth(),
      ]);
      setSummary(sumRes.data);
      setByVet(vetRes.data);
      setByAnimal(animalRes.data);
      setByMonth(monthRes.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(filter); }, 0);
    return () => window.clearTimeout(timer);
  }, [load, filter]);

  const handleRangeChange = (dates: [dayjs.Dayjs | null, dayjs.Dayjs | null] | null) => {
    if (dates && dates[0] && dates[1]) {
      setFilter({ from: dates[0].format('YYYY-MM-DD'), to: dates[1].format('YYYY-MM-DD') });
    } else {
      setFilter({});
    }
  };

  const vetColumns: ColumnsType<HealthCostByVet> = [
    { title: t('vetName2'), dataIndex: 'vetName' },
    { title: t('records'), dataIndex: 'recordCount', width: 100 },
    {
      title: t('totalCost'),
      dataIndex: 'totalCost',
      width: 140,
      render: (c: number) => <Tag color="green">{formatMoney(c)}</Tag>,
    },
  ];

  const animalColumns: ColumnsType<HealthCostByAnimal> = [
    {
      title: t('animal'),
      render: (_, r) => (
        <span>
          {r.tagNumber}
          {r.animalName && <span style={{ color: '#999' }}> ({r.animalName})</span>}
        </span>
      ),
    },
    { title: t('records'), dataIndex: 'recordCount', width: 100 },
    {
      title: t('totalCost'),
      dataIndex: 'totalCost',
      width: 140,
      render: (c: number) => <Tag color="green">{formatMoney(c)}</Tag>,
    },
  ];

  const monthColumns: ColumnsType<HealthCostByMonth> = [
    { title: t('month'), dataIndex: 'monthName' },
    {
      title: t('medical'),
      dataIndex: 'medicalCost',
      width: 120,
      render: (c: number) => c > 0 ? formatMoney(c) : '-',
    },
    {
      title: t('vaccination'),
      dataIndex: 'vaccinationCost',
      width: 120,
      render: (c: number) => c > 0 ? formatMoney(c) : '-',
    },
    {
      title: t('total'),
      dataIndex: 'total',
      width: 120,
      render: (c: number) => <strong>{c > 0 ? formatMoney(c) : '-'}</strong>,
    },
  ];

  return (
    <div style={{ padding: 0 }}>
      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12} lg={6}>
          <Card loading={loading}>
            <Statistic
              title={t('totalVetCost')}
              value={summary?.grandTotal ?? 0}
              precision={2}
              prefix={<DollarOutlined />}
              valueStyle={{ color: '#1677ff' }}
            />
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card loading={loading}>
            <Statistic
              title={t('medicalCosts')}
              value={summary?.totalMedicalCost ?? 0}
              precision={2}
              prefix={<MedicineBoxOutlined />}
              suffix={<span style={{ fontSize: 14, color: '#999' }}>({summary?.medicalRecordCount ?? 0})</span>}
            />
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card loading={loading}>
            <Statistic
              title={t('vaccinationCosts')}
              value={summary?.totalVaccinationCost ?? 0}
              precision={2}
              prefix={<ExperimentOutlined />}
              suffix={<span style={{ fontSize: 14, color: '#999' }}>({summary?.vaccinationRecordCount ?? 0})</span>}
            />
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card loading={loading}>
            <Statistic
              title={t('recordsWithCost')}
              value={(summary?.medicalRecordCount ?? 0) + (summary?.vaccinationRecordCount ?? 0)}
              prefix={<TeamOutlined />}
            />
          </Card>
        </Col>
      </Row>

      <Card
        title={t('costBreakdownByVet')}
        style={{ marginTop: 16 }}
        extra={<RangePicker onChange={handleRangeChange} />}
      >
        <Table
          rowKey="vetName"
          columns={vetColumns}
          dataSource={byVet}
          loading={loading}
          pagination={false}
          size="small"
        />
      </Card>

      <Card title={t('costBreakdownByAnimal')} style={{ marginTop: 16 }}>
        <Table
          rowKey="animalId"
          columns={animalColumns}
          dataSource={byAnimal}
          loading={loading}
          pagination={byAnimal.length > 10 ? { pageSize: 10 } : false}
          size="small"
        />
      </Card>

      <Card title={t('monthlyCostsThisYear')} style={{ marginTop: 16 }}>
        <Table
          rowKey="month"
          columns={monthColumns}
          dataSource={byMonth}
          loading={loading}
          pagination={false}
          size="small"
        />
      </Card>
    </div>
  );
};

export default VetCostsPage;
