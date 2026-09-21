import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Col, Row, Space, Statistic, Table, Tag, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { jobsApi } from '../../api/jobs';
import type { FailedJobStatus, JobStatus, RecurringJobStatus } from '../../api/jobs';

const { Title, Text, Paragraph } = Typography;

const stateColors: Record<string, string> = {
  Succeeded: 'green',
  Processing: 'blue',
  Enqueued: 'blue',
  Scheduled: 'gold',
  Failed: 'red',
  Deleted: 'default',
};

const formatUtc = (value?: string | null) => (value ? dayjs(value).format('MMM D, HH:mm:ss [UTC]') : '—');

const recurringColumns: ColumnsType<RecurringJobStatus> = [
  {
    title: 'Job',
    dataIndex: 'id',
    key: 'id',
    render: (id: string) => <Text code>{id}</Text>,
  },
  {
    title: 'Schedule',
    dataIndex: 'cron',
    key: 'cron',
    render: (cron: string) => <Text code>{cron}</Text>,
  },
  {
    title: 'Next run',
    dataIndex: 'nextRunUtc',
    key: 'nextRunUtc',
    render: formatUtc,
  },
  {
    title: 'Last run',
    dataIndex: 'lastRunUtc',
    key: 'lastRunUtc',
    render: formatUtc,
  },
  {
    title: 'Last state',
    dataIndex: 'lastState',
    key: 'lastState',
    render: (state?: string | null) =>
      state ? <Tag color={stateColors[state] ?? 'default'}>{state}</Tag> : <Text type="secondary">Not run yet</Text>,
  },
  {
    title: 'Last error',
    dataIndex: 'lastError',
    key: 'lastError',
    render: (error?: string | null) =>
      error ? <Text type="danger">{error}</Text> : <Text type="secondary">—</Text>,
  },
];

const failureColumns: ColumnsType<FailedJobStatus> = [
  { title: 'Job', dataIndex: 'job', key: 'job', render: (job?: string | null) => job ?? <Text type="secondary">Unknown</Text> },
  { title: 'Failed at', dataIndex: 'failedAtUtc', key: 'failedAtUtc', render: formatUtc },
  { title: 'Exception', dataIndex: 'exceptionType', key: 'exceptionType' },
  {
    title: 'Message',
    dataIndex: 'exceptionMessage',
    key: 'exceptionMessage',
    render: (message?: string | null) => (message ? <Text type="danger">{message}</Text> : '—'),
  },
];

/**
 * Scheduled job status for a SystemOwner.
 *
 * Exists so a failing or stopped scheduler is visible instead of silently
 * leaving stale data behind (the Phase 1.2 observability principle, extended to
 * background work). Read-only on purpose: jobs are configured by deployment
 * settings, not from the UI.
 */
const JobsPage: React.FC = () => {
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
      setError('Could not load the job status.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
        <div>
          <Title level={3} style={{ marginBottom: 0 }}>Scheduled Jobs</Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Recurring background work: daily feeding-task generation and hourly health-status recalculation.
            Times are UTC. Last read {status ? formatUtc(status.readAtUtc) : '—'}.
          </Paragraph>
        </div>
        <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>
          Refresh
        </Button>
      </Space>

      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}

      {status && !status.enabled && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message="Background jobs are disabled in this process"
          description="Jobs:Enabled is false, so no scheduled work runs here. Feeding tasks and health-status snapshots will not refresh until it is enabled."
        />
      )}

      {status?.warnings.map((warning) => (
        <Alert key={warning} type="warning" showIcon message={warning} style={{ marginBottom: 16 }} />
      ))}

      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title="Enqueued" value={status?.counters.enqueued ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title="Processing" value={status?.counters.processing ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title="Scheduled" value={status?.counters.scheduled ?? 0} /></Card></Col>
        <Col xs={12} md={8} lg={4}>
          <Card size="small"><Statistic title="Failed" value={status?.counters.failed ?? 0} valueStyle={{ color: (status?.counters.failed ?? 0) > 0 ? '#cf1322' : undefined }} /></Card>
        </Col>
        <Col xs={12} md={8} lg={4}><Card size="small"><Statistic title="Succeeded" value={status?.counters.succeeded ?? 0} /></Card></Col>
      </Row>

      <Card title="Recurring jobs" size="small" style={{ marginBottom: 16 }}>
        <Table<RecurringJobStatus>
          rowKey="id"
          size="small"
          loading={loading}
          columns={recurringColumns}
          dataSource={status?.recurringJobs ?? []}
          pagination={false}
          locale={{ emptyText: status?.enabled ? 'No recurring jobs registered.' : 'Jobs are disabled in this process.' }}
        />
      </Card>

      <Card title="Recent failures" size="small">
        <Table<FailedJobStatus>
          rowKey="id"
          size="small"
          loading={loading}
          columns={failureColumns}
          dataSource={status?.recentFailures ?? []}
          pagination={false}
          locale={{ emptyText: 'No failed executions.' }}
        />
      </Card>
    </div>
  );
};

export default JobsPage;
