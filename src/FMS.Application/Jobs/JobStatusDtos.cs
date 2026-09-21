namespace FMS.Application.Jobs;

/// <summary>
/// Supplies the job scheduler's status for the SystemOwner-facing status view.
///
/// Abstracted so the API endpoint can be tested without a job store, and so a
/// disabled scheduler is reported honestly instead of throwing.
/// </summary>
public interface IJobStatusProvider
{
    Task<JobStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Snapshot of the scheduler's state, rendered by the admin job-status view.</summary>
public class JobStatusDto
{
    /// <summary>False when <c>Jobs:Enabled</c> is off — jobs are not running in this process.</summary>
    public bool Enabled { get; set; }

    /// <summary>When this status was read (UTC).</summary>
    public DateTime ReadAtUtc { get; set; } = DateTime.UtcNow;

    public JobCountersDto Counters { get; set; } = new();

    public List<RecurringJobStatusDto> RecurringJobs { get; set; } = new();

    /// <summary>Most recent failed executions, so a broken job is visible rather than silent.</summary>
    public List<FailedJobStatusDto> RecentFailures { get; set; } = new();

    /// <summary>Non-fatal problems encountered while reading the status (for example an unreachable job store).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Queue-wide counters from the job store.</summary>
public class JobCountersDto
{
    public int Enqueued { get; set; }
    public int Processing { get; set; }
    public int Scheduled { get; set; }
    public int Failed { get; set; }
    public int Succeeded { get; set; }
}

public class RecurringJobStatusDto
{
    public string Id { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;
    public string? Queue { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }

    /// <summary>Hangfire state name of the last execution (Succeeded, Failed, Processing, …).</summary>
    public string? LastState { get; set; }

    /// <summary>Last error recorded for this recurring job, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>True when the scheduler still has this job registered.</summary>
    public bool Registered { get; set; } = true;
}

public class FailedJobStatusDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary>CLR type name of the job that failed (e.g. <c>FeedingTaskGenerationJob</c>).</summary>
    public string? Job { get; set; }

    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? Reason { get; set; }
}
