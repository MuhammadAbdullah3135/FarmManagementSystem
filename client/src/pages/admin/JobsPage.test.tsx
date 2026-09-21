import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import JobsPage from './JobsPage';
import { jobsApi } from '../../api/jobs';
import type { JobStatus } from '../../api/jobs';

vi.mock('../../api/jobs', () => ({
  jobsApi: { status: vi.fn() },
}));

const enabledStatus: JobStatus = {
  enabled: true,
  readAtUtc: '2026-09-19T10:00:00Z',
  counters: { enqueued: 2, processing: 1, scheduled: 4, failed: 3, succeeded: 42 },
  recurringJobs: [
    {
      id: 'feeding-task-generation-fan-out',
      cron: '5 0 * * *',
      queue: 'default',
      nextRunUtc: '2026-09-20T00:05:00Z',
      lastRunUtc: '2026-09-19T00:05:00Z',
      lastState: 'Succeeded',
      lastError: null,
      registered: true,
    },
    {
      id: 'health-status-recalculation-fan-out',
      cron: '0 * * * *',
      queue: 'default',
      nextRunUtc: '2026-09-19T11:00:00Z',
      lastRunUtc: '2026-09-19T10:00:00Z',
      lastState: 'Failed',
      lastError: 'Npgsql.NpgsqlException: connection refused',
      registered: true,
    },
  ],
  recentFailures: [
    {
      id: 'job-1',
      job: 'HealthStatusRecalculationJob',
      exceptionType: 'InvalidOperationException',
      exceptionMessage: 'Job health-status-recalculation failed for farm 1111: Unexpected - boom',
      failedAtUtc: '2026-09-19T10:00:01Z',
      reason: null,
    },
  ],
  warnings: [],
};

const disabledStatus: JobStatus = {
  ...enabledStatus,
  enabled: false,
  counters: { enqueued: 0, processing: 0, scheduled: 0, failed: 0, succeeded: 0 },
  recurringJobs: [],
  recentFailures: [],
  warnings: ['Background jobs are disabled in this process (Jobs:Enabled = false). Scheduled work is not running here.'],
};

beforeEach(() => {
  vi.mocked(jobsApi.status).mockResolvedValue({ data: enabledStatus } as never);
});

describe('JobsPage', () => {
  it('shows the recurring jobs, their schedule and the last error of a failing job', async () => {
    render(<JobsPage />);

    expect(await screen.findByText('feeding-task-generation-fan-out')).toBeInTheDocument();
    expect(screen.getByText('health-status-recalculation-fan-out')).toBeInTheDocument();
    expect(screen.getByText('5 0 * * *')).toBeInTheDocument();

    // State tags ('Succeeded'/'Failed' also appear as counter card titles, hence
    // the tag-scoped lookup).
    const stateTags = screen
      .getAllByText(/^(Succeeded|Failed)$/)
      .filter((element) => element.closest('.ant-tag') !== null)
      .map((element) => element.textContent);

    expect(stateTags).toContain('Succeeded');

    // The failure must be visible rather than silent.
    expect(stateTags).toContain('Failed');
    expect(screen.getByText('Npgsql.NpgsqlException: connection refused')).toBeInTheDocument();
  });

  it('lists recent failed executions so a broken job is not silent', async () => {
    render(<JobsPage />);

    expect(await screen.findByText('HealthStatusRecalculationJob')).toBeInTheDocument();
    expect(
      screen.getByText('Job health-status-recalculation failed for farm 1111: Unexpected - boom'),
    ).toBeInTheDocument();
  });

  it('explains itself when jobs are disabled in this process', async () => {
    vi.mocked(jobsApi.status).mockResolvedValue({ data: disabledStatus } as never);

    render(<JobsPage />);

    expect(
      await screen.findByText('Background jobs are disabled in this process'),
    ).toBeInTheDocument();
    expect(jobsApi.status).toHaveBeenCalled();
  });
});
