using FMS.Application.Jobs;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Options;

namespace FMS.API.Jobs;

/// <summary>
/// Reads job status from Hangfire's own storage.
///
/// Extends the Phase 1.2 observability principle — degradation is reported, not
/// hidden — from dashboard metrics to scheduled work: a job that fails or stops
/// running has to be visible to a SystemOwner rather than silently leaving
/// stale data behind.
/// </summary>
public class HangfireJobStatusProvider : IJobStatusProvider
{
    private const int MaxRecurringJobs = 100;
    private const int MaxRecentFailures = 10;

    private readonly JobStorage _storage;
    private readonly IOptions<JobOptions> _options;
    private readonly ILogger<HangfireJobStatusProvider> _logger;

    public HangfireJobStatusProvider(
        JobStorage storage,
        IOptions<JobOptions> options,
        ILogger<HangfireJobStatusProvider> logger)
    {
        _storage = storage;
        _options = options;
        _logger = logger;
    }

    public Task<JobStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var status = new JobStatusDto
        {
            Enabled = options.Enabled,
            ReadAtUtc = DateTime.UtcNow
        };

        if (!options.Enabled)
        {
            // Jobs:Enabled=false means this process runs no scheduler at all.
            // Saying so is more useful than an empty list with no explanation,
            // and it keeps the endpoint from touching the job store.
            status.Warnings.Add(
                "Background jobs are disabled in this process (Jobs:Enabled = false). Scheduled work is not running here.");
            return Task.FromResult(status);
        }

        try
        {
            var monitoring = _storage.GetMonitoringApi();
            var statistics = monitoring.GetStatistics();

            status.Counters = new JobCountersDto
            {
                Enqueued = (int)statistics.Enqueued,
                Processing = (int)statistics.Processing,
                Scheduled = (int)statistics.Scheduled,
                Failed = (int)statistics.Failed,
                Succeeded = (int)statistics.Succeeded
            };

            // Recurring jobs are read from the storage connection (Hangfire 1.8
            // no longer exposes them on the monitoring API), capped so a runaway
            // registry cannot make this endpoint expensive.
            using (var connection = _storage.GetConnection())
            {
                status.RecurringJobs = connection
                    .GetRecurringJobs()
                    .Take(MaxRecurringJobs)
                    .Select(ToRecurringJobStatus)
                    .OrderBy(j => j.Id, StringComparer.Ordinal)
                    .ToList();
            }

            status.RecentFailures = monitoring
                .FailedJobs(0, MaxRecentFailures)
                .Select(item => ToFailedJobStatus(item.Key, item.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            // An unreachable job store must not turn the status view into a 500:
            // the view exists precisely to explain trouble.
            _logger.LogError(ex, "Could not read job status from the Hangfire storage.");
            status.Warnings.Add($"Could not read job status from the job store: {ex.Message}");
        }

        return Task.FromResult(status);
    }

    private static RecurringJobStatusDto ToRecurringJobStatus(RecurringJobDto job) => new()
    {
        Id = job.Id,
        Cron = job.Cron,
        Queue = job.Queue,
        NextRunUtc = job.NextExecution,
        LastRunUtc = job.LastExecution,
        LastState = job.LastJobState,
        LastError = job.Error
    };

    private static FailedJobStatusDto ToFailedJobStatus(string id, FailedJobDto job) => new()
    {
        Id = id,
        Job = job.Job?.Type?.Name,
        ExceptionType = job.ExceptionType,
        ExceptionMessage = job.ExceptionMessage,
        FailedAtUtc = job.FailedAt,
        Reason = job.Reason
    };
}
