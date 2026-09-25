import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Button, Card, Col, Row, Space, Statistic, Table, Tag, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { jobsApi } from '../../api/jobs';
import type { FailedJobStatus, JobStatus, RecurringJobStatus } from '../../api/jobs';
import { formatDateLongWithSeconds } from '../../i18n/format';
import { useTranslation } from 'react-i18next';

const { Title, Text, Paragraph } = Typography;

/** Server-side job states are values, not copy; the tag colours key off them. */
const STATE_COLORS: Record<string, string> = {
  Succeeded: 'green',
  Processing: 'blue',
  Enqueued: 'blue',
  Scheduled: 'gold',
  Failed: 'red',
  Deleted: 'default',
};

/**
 * The key that names each state. The counters above the tables already say these words, so
 * a state is not translated twice and cannot drift between the two places it appears.
 */
const STATE_KEYS: Record<string, string> = {
  Succeeded: 'succeeded',
  Processing: 'processing',
  Enqueued: 'enqueued',
  Scheduled: 'scheduled',
  Failed: 'failed',
  Deleted: 'deleted',
};

const UTC_SUFFIX = 'UTC';

/**
 * Scheduled job status for a SystemOwner.
 *
 * Exists so a failing or stopped scheduler is visible instead of silently
 * leaving stale data behind (the Phase 1.2 observability principle, extended to
 * background work). Read-only on purpose: jobs are configured by deployment
 * settings, not from the UI.
 */
const JobsPage: React.FC = () => {const { t } = useTranslation('admin'); 
  const [status, setStatus] = useState<JobStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await jobsApi.status();
      setStatus(response.data);
      setError(null);
    } catch {
      // The axios interceptor surfaces the failure; keep the page mounted.
      setError(t('loadFailed'));
    }
  }, [t]);

  useEffect(() => {
    void load();
  }, [load]);

  // Timestamps are labelled UTC because that is what the scheduler reports in; the
  // formatting itself (month name, order) follows the reader's language.
  const formatUtc = useCallback(
    (value?: string | null) => (value ? `${formatDateLongWithSeconds(value)} ${UTC_SUFFIX}` : '—'),
    [],
  );

  // Inside the component because they carry translated titles — a module-level column
  // definition cannot call `t`, and the English it would otherwise bake in is exactly the
  // kind of string nothing catches at build time.
  const recurringColumns: ColumnsType<RecurringJobStatus> = useMemo(() => [
    {
      title: t('job'),
      dataIndex: 'id',
      key: 'id',
      render: (id: string) => <Text code>{id}</Text>,
    },
    {
      title: t('schedule'),
      dataIndex: 'cron',
      key: 'cron',
      render: (cron: string) => <Text code>{cron}</Text>,
    },
    {
      title: t('nextRun'),
      dataIndex: 'nextRunUtc',
      key: 'nextRunUtc',
      render: formatUtc,
    },
    {
      title: t('lastRun'),
      dataIndex: 'lastRunUtc',
      key: 'lastRunUtc',
      render: formatUtc,
    },
    {
      title: t('lastState'),
      dataIndex: 'lastState',
      key: 'lastState',
      render: (state?: string | null) =>
        state ? (
          <Tag color={STATE_COLORS[state] ?? 'default'}>
            {STATE_KEYS[state] ? t(STATE_KEYS[state]) : state}
          </Tag>
        ) : (
          <Text type="secondary">{t('notRunYet')}</Text>
        ),
    },
    {
      title: t('lastError'),
      dataIndex: 'lastError',
      key: 'lastError',
      render: (jobError?: string | null) =>
        jobError ? <Text type="danger">{jobError}</Text> : <Text type="secondary">—</Text>,
    },
  ], [t, formatUtc]);

  const failureColumns: ColumnsType<FailedJobStatus> = useMemo(() => [
    { title: t('job'), dataIndex: 'job', key: 'job', render: (job?: string | null) => job ?? <Text type="secondary">{t('unknown')}</Text> },
    { title: t('failedAt'), dataIndex: 'failedAtUtc', key: 'failedAtUtc', render: formatUtc },
    { title: t('exception'), dataIndex: 'exceptionType', key: 'exceptionType' },
    {
      title: t('message'),
      dataIndex: 'exceptionMessage',
      key: 'exceptionMessage',
      render: (message?: string | null) => (message ? <Text type="danger">{message}</Text> : '—'),
    },
  ], [t, formatUtc]);

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
        <div>
          <Title level={3} style={{ marginBottom: 0 }}>{t('scheduledJobs')}</Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('recurringBackgroundWorkDailyFeedingTaskGenerationAnd')} {status ? formatUtc(status.readAtUtc) : '—'}.
          </Paragraph>
        </div>
        <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>
          {t('refresh')}
        </Button>
      </Space>

      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}

      {status && !status.enabled && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('backgroundJobsAreDisabledInThisProcess')}
          description={t('jobsEnabledIsFalseSoNoScheduledWork')}
        />
      )}

      {status?.warnings.map((warning) => (
        <Alert key={warning} type="warning" showIcon message={warning} style={{ marginBottom: 16 }} />
      ))}

      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title={t('enqueued')} value={status?.counters.enqueued ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title={t('processing')} value={status?.counters.processing ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title={t('scheduled')} value={status?.counters.scheduled ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}>
          <Card size="small"><Statistic title={t('failed')} value={status?.counters.failed ?? 0} valueStyle={{ color: (status?.counters.failed ?? 0) > 0 ? '#cf1322' : undefined }} /></Card>
        </Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title={t('succeeded')} value={status?.counters.succeeded ?? 0} /></Card></Col>
      </Row>

      <Card title={t('recurringJobs')} size="small" style={{ marginBottom: 16 }}>
        <Table<RecurringJobStatus>
          rowKey="id"
          size="small"
          loading={loading}
          columns={recurringColumns}
          dataSource={status?.recurringJobs ?? []}
          pagination={false}
          locale={{ emptyText: status?.enabled ? t('noRecurringJobs') : t('jobsDisabledHere') }}
        />
      </Card>

      <Card title={t('recentFailures')} size="small">
        <Table<FailedJobStatus>
          rowKey="id"
          size="small"
          loading={loading}
          columns={failureColumns}
          dataSource={status?.recentFailures ?? []}
          pagination={false}
          locale={{ emptyText: t('noFailedExecutions') }}
        />
      </Card>
    </div>
  );
};

export default JobsPage;
