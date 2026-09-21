using FMS.Application.Jobs;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// Stands in for the Hangfire-backed provider in the test host, which runs no
/// scheduler for real. The default payload deliberately contains a failing
/// recurring job and a recent failure so the status endpoint's failure
/// visibility can be asserted end to end.
/// </summary>
public sealed class FakeJobStatusProvider : IJobStatusProvider
{
    public JobStatusDto Status { get; set; } = new()
    {
        Enabled = true,
        Counters = new JobCountersDto
        {
            Enqueued = 1,
            Processing = 0,
            Scheduled = 2,
            Failed = 3,
            Succeeded = 40
        },
        RecurringJobs = new List<RecurringJobStatusDto>
        {
            new()
            {
                Id = JobNames.FeedingTaskGenerationFanOut,
                Cron = "5 0 * * *",
                Queue = "default",
                NextRunUtc = DateTime.UtcNow.AddHours(1),
                LastRunUtc = DateTime.UtcNow.AddHours(-23),
                LastState = "Succeeded"
            },
            new()
            {
                Id = JobNames.HealthStatusRecalculationFanOut,
                Cron = "0 * * * *",
                Queue = "default",
                NextRunUtc = DateTime.UtcNow.AddHours(1),
                LastRunUtc = DateTime.UtcNow.AddMinutes(-30),
                LastState = "Failed",
                LastError = "Npgsql.NpgsqlException: connection refused"
            }
        },
        RecentFailures = new List<FailedJobStatusDto>
        {
            new()
            {
                Id = "failed-job-1",
                Job = nameof(Infrastructure.Jobs.HealthStatusRecalculationJob),
                ExceptionType = "InvalidOperationException",
                ExceptionMessage = "Job health-status-recalculation failed for farm 6a4c0e2f: Unexpected - boom",
                FailedAtUtc = DateTime.UtcNow.AddMinutes(-30)
            }
        }
    };

    public Task<JobStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Status);
}
