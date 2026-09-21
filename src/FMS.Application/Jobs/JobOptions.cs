namespace FMS.Application.Jobs;

/// <summary>
/// Background job configuration, bound from the <c>Jobs</c> configuration
/// section. Every value has a safe default so the API runs with no
/// configuration at all.
/// </summary>
public class JobOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// Master switch for *running* jobs in this process. When false no Hangfire
    /// server is started and no recurring job is registered; the storage is
    /// still configured so the status endpoint can report the scheduler as
    /// disabled instead of failing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Automatic retry attempts per failed job execution (Hangfire default is 10).</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Daily feeding-task materialisation. Cron is evaluated in UTC.</summary>
    public JobScheduleOptions FeedingTaskGeneration { get; set; } = new()
    {
        Cron = JobCron.DailyAt(0, 5)
    };

    /// <summary>Hourly health-status (due/overdue vaccination + weight check) recalculation.</summary>
    public JobScheduleOptions HealthStatusRecalculation { get; set; } = new()
    {
        Cron = JobCron.Hourly()
    };

    /// <summary>
    /// Notification dispatch. Faster than the health job because its output is
    /// what a person is waiting to be told about — a quarter-hour is the granularity
    /// at which "the vet will see this today" still holds.
    /// </summary>
    public JobScheduleOptions NotificationDispatch { get; set; } = new()
    {
        Cron = JobCron.EveryMinutes(15)
    };

    public HealthSnapshotOptions HealthSnapshot { get; set; } = new();
}

/// <summary>Per-job schedule. <see cref="Cron"/> uses standard 5-field cron syntax, UTC.</summary>
public class JobScheduleOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>5-field cron expression (minute hour day month day-of-week).</summary>
    public string Cron { get; set; } = JobCron.Hourly();
}

/// <summary>Freshness rules for the dashboard's health-snapshot fast path.</summary>
public class HealthSnapshotOptions
{
    /// <summary>
    /// A snapshot older than this is treated as stale and the dashboard falls
    /// back to live calculation. Default is two job intervals, so a single
    /// missed hourly run does not push the dashboard off the fast path.
    /// </summary>
    public int StalenessMinutes { get; set; } = 120;
}

/// <summary>Small helpers for the cron expressions used as defaults.</summary>
public static class JobCron
{
    public static string Hourly() => "0 * * * *";

    public static string DailyAt(int hour, int minute) => $"{minute} {hour} * * *";

    /// <summary>Every <paramref name="minutes"/> minutes, offset within the hour when needed.</summary>
    public static string EveryMinutes(int minutes) => minutes >= 60
        ? Hourly()
        : minutes <= 1
            ? "* * * * *"
            : $"*/{minutes} * * * *";
}
