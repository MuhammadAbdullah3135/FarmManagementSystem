namespace FMS.Application.Jobs;

/// <summary>
/// Canonical identifiers for the recurring background jobs. Used for Hangfire
/// recurring-job ids and for every structured log line a job emits, so logs,
/// the job-status endpoint and the Hangfire dashboard all name jobs identically.
/// </summary>
public static class JobNames
{
    /// <summary>Fan-out: enqueues one feed-task generation job per active farm.</summary>
    public const string FeedingTaskGenerationFanOut = "feeding-task-generation-fan-out";

    /// <summary>Per farm: materialises feeding tasks for the current UTC day.</summary>
    public const string FeedingTaskGeneration = "feeding-task-generation";

    /// <summary>Fan-out: enqueues one health-status recalculation job per active farm.</summary>
    public const string HealthStatusRecalculationFanOut = "health-status-recalculation-fan-out";

    /// <summary>Per farm: refreshes the due/overdue health status snapshot.</summary>
    public const string HealthStatusRecalculation = "health-status-recalculation";

    /// <summary>Fan-out: enqueues one notification dispatch job per active farm.</summary>
    public const string NotificationDispatchFanOut = "notification-dispatch-fan-out";

    /// <summary>Per farm: reconciles notifications from the farm's current alert conditions.</summary>
    public const string NotificationDispatch = "notification-dispatch";

    /// <summary>
    /// Per farm, on demand: builds the farm's full-data export archive.
    ///
    /// The only job with no cron — it is enqueued by the export endpoint when a
    /// member asks for one, which is why it is named here alongside the scheduled
    /// jobs rather than in a separate vocabulary: it must appear in logs and the
    /// job-status view under the same naming rules as the rest.
    /// </summary>
    public const string FarmExport = "farm-export";
}
