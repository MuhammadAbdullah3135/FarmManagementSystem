using FMS.Application.Finance;
using FMS.Application.Feed;

namespace FMS.Application.Dashboard;

// ── Summary ─────────────────────────────────────────────

public class DashboardSummaryDto
{
    public int TotalAnimals { get; set; }
    public Dictionary<string, int> AnimalsByType { get; set; } = new();
    public Dictionary<string, int> AnimalsByStatus { get; set; } = new();
    public int PregnantCount { get; set; }
    public int SickCount { get; set; }
    public int DueVaccinationCount { get; set; }
    public int DueWeightCheckCount { get; set; }
    public int OverdueTasks { get; set; }
    public int UpcomingBirths { get; set; }
    public decimal TotalFeedStockValue { get; set; }
    public decimal TotalInventoryStockValue { get; set; }

    /// <summary>
    /// When the background health-status job last computed the due/overdue
    /// vaccination and weight-check counts served above. Null means the
    /// snapshot was missing or stale, so these counts came from a live
    /// calculation on this request — the values are equally correct either way.
    /// </summary>
    public DateTime? HealthSnapshotComputedAtUtc { get; set; }

    /// <summary>
    /// Metrics that could not be calculated on this request (provider
    /// incompatibility or unexpected failure). Values for these metrics
    /// default to zero and MUST NOT be treated as real counts.
    /// </summary>
    public List<string> DegradedMetrics { get; set; } = new();
}

/// <summary>
/// Canonical metric names reported in DashboardSummaryDto.DegradedMetrics.
///
/// The three alert-only names below are never reported on the summary; they
/// exist so the alert computation can say which of its inputs failed. The
/// notification dispatcher needs that distinction — "no overdue vaccinations"
/// and "the overdue-vaccination query failed" must not look the same, or a
/// transient failure would resolve live notifications.
/// </summary>
public static class DashboardMetricNames
{
    public const string DueVaccinationCount = "DueVaccinationCount";
    public const string DueWeightCheckCount = "DueWeightCheckCount";
    public const string UpcomingBirths = "UpcomingBirths";
    public const string OverdueVaccinations = "OverdueVaccinations";
    public const string OverdueWeightChecks = "OverdueWeightChecks";
    public const string DueBirthAlerts = "DueBirthAlerts";
    public const string MedicineAlerts = "MedicineAlerts";
    public const string OverdueTasks = "OverdueTasks";
    public const string LowInventory = "LowInventory";
}

// ── Alerts ──────────────────────────────────────────────

public class DashboardAlertDto
{
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Critical, Warning, Info
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string? Link { get; set; }

    /// <summary>
    /// Stable identity of the underlying condition (see
    /// <c>NotificationAlertTypes.SourceKeys</c>), built from entity ids only.
    ///
    /// The notification dispatcher dedupes on this: a condition whose details
    /// change (a pushed-back due date, a new severity) is the same condition, so
    /// it refreshes one notification instead of raising another.
    /// </summary>
    public string? SourceKey { get; set; }
}

/// <summary>
/// The dashboard's alert set plus the alert types that could not be evaluated on
/// this pass.
///
/// <see cref="GetAlertsAsync"/> collapses both cases into "not present", which is
/// right for a page load — a missing alert is a missing alert. The notification
/// dispatcher cannot do that: absent-because-failed would read as
/// absent-because-fixed, and every live notification for that type would be
/// resolved by a momentary query failure.
/// </summary>
public class DashboardAlertSetDto
{
    public List<DashboardAlertDto> Alerts { get; set; } = new();

    /// <summary>Alert types whose inputs failed to compute, and must not be reconciled.</summary>
    public List<string> UnavailableAlertTypes { get; set; } = new();
}

// ── Charts ──────────────────────────────────────────────

public class DashboardChartsDto
{
    public List<AnimalTrendPoint> AnimalTrends { get; set; } = new();
    public CategoryBreakdownReport? ExpenseBreakdown { get; set; }
    public List<ConsumptionTrendPointDto> FeedConsumptionTrend { get; set; } = new();
    public List<MonthlySummaryItem> MonthlyPL { get; set; } = new();
}

public class AnimalTrendPoint
{
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}
