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
}
