import api from './axios';

/**
 * Scheduler status, mirroring `FMS.Application.Jobs.JobStatusDto`.
 *
 * Read-only, and deliberately account-scoped (`/api/admin/jobs`): the scheduler
 * is infrastructure rather than farm data, so the endpoint is not farm-scoped
 * and keeps the caller's account roles.
 */
export interface JobCounters {
  enqueued: number;
  processing: number;
  scheduled: number;
  failed: number;
  succeeded: number;
}

export interface RecurringJobStatus {
  id: string;
  cron: string;
  queue?: string | null;
  nextRunUtc?: string | null;
  lastRunUtc?: string | null;
  lastState?: string | null;
  lastError?: string | null;
  registered: boolean;
}

export interface FailedJobStatus {
  id: string;
  job?: string | null;
  exceptionType?: string | null;
  exceptionMessage?: string | null;
  failedAtUtc?: string | null;
  reason?: string | null;
}

export interface JobStatus {
  enabled: boolean;
  readAtUtc: string;
  counters: JobCounters;
  recurringJobs: RecurringJobStatus[];
  recentFailures: FailedJobStatus[];
  warnings: string[];
}

export const jobsApi = {
  status: () => api.get<JobStatus>('/admin/jobs'),
};
