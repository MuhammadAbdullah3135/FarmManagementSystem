using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// Per-farm cache of the due/overdue health counts computed by the
/// health-status recalculation job (the <c>health-status-recalculation</c>
/// recurring Hangfire job).
///
/// Exactly one row per farm, refreshed by the job. The dashboard reads it as a
/// fast path and falls back to live calculation when the row is missing or
/// stale, so a stopped job degrades freshness, never correctness.
///
/// Internal telemetry, not business data: excluded from the audit log.
/// </summary>
public class FarmHealthStatusSnapshot : BaseEntity, IAuditLogExcluded
{
    public Guid FarmId { get; set; }

    /// <summary>Animal-vaccine pairs whose next dose is due within the schedule window (status Due).</summary>
    public int DueVaccinationCount { get; set; }

    /// <summary>Animal-vaccine pairs already past their next dose date (status Overdue).</summary>
    public int OverdueVaccinationCount { get; set; }

    /// <summary>Animal weight checks that are due (status Due).</summary>
    public int DueWeightCheckCount { get; set; }

    /// <summary>Animal weight checks already past their due date (status Overdue).</summary>
    public int OverdueWeightCheckCount { get; set; }

    /// <summary>When the job last computed these counts (UTC).</summary>
    public DateTime ComputedAtUtc { get; set; }

    public Farm Farm { get; set; } = null!;
}
