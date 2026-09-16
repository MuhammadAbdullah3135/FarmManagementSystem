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
    /// Metrics that could not be calculated on this request (provider
    /// incompatibility or unexpected failure). Values for these metrics
    /// default to zero and MUST NOT be treated as real counts.
    /// </summary>
    public List<string> DegradedMetrics { get; set; } = new();
}

/// <summary>
/// Canonical metric names reported in DashboardSummaryDto.DegradedMetrics.
/// </summary>
public static class DashboardMetricNames
{
    public const string DueVaccinationCount = "DueVaccinationCount";
    public const string DueWeightCheckCount = "DueWeightCheckCount";
    public const string UpcomingBirths = "UpcomingBirths";
    public const string OverdueVaccinations = "OverdueVaccinations";
    public const string OverdueWeightChecks = "OverdueWeightChecks";
    public const string DueBirthAlerts = "DueBirthAlerts";
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
