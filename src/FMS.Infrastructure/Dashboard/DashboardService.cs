using FMS.Application.Common;
using FMS.Application.Dashboard;
using FMS.Application.Feed;
using FMS.Application.Finance;
using FMS.Application.Health;
using FMS.Application.Inventory;
using FMS.Application.Tasks;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IBreedingSvc = FMS.Application.Breeding.IBreedingService;
using GestationFilter = FMS.Application.Breeding.GestationRecordListFilter;

namespace FMS.Infrastructure.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly FmsDbContext _db;
    private readonly IVaccineService _vaccineService;
    private readonly IMedicineService _medicineService;
    private readonly IFarmTaskService _taskService;
    private readonly IBreedingSvc _breedingService;
    private readonly IInventoryService _inventoryService;
    private readonly IFeedService _feedService;
    private readonly IFinanceService _financeService;
    private readonly IWeightCheckScheduleService _weightCheckService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        FmsDbContext db,
        IVaccineService vaccineService,
        IMedicineService medicineService,
        IFarmTaskService taskService,
        IBreedingSvc breedingService,
        IInventoryService inventoryService,
        IFeedService feedService,
        IFinanceService financeService,
        IWeightCheckScheduleService weightCheckService,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _vaccineService = vaccineService;
        _medicineService = medicineService;
        _taskService = taskService;
        _breedingService = breedingService;
        _inventoryService = inventoryService;
        _feedService = feedService;
        _financeService = financeService;
        _weightCheckService = weightCheckService;
        _logger = logger;
    }

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid farmId)
    {
        // ── Animal aggregates (DbContext direct — simple COUNT/GROUP BY) ──
        var animals = await _db.Animals
            .Where(a => a.FarmId == farmId && !a.IsDeleted)
            .Select(a => new { TypeName = a.AnimalType.Name, StatusName = a.AnimalStatus.Name, Category = a.AnimalStatus.Category })
            .ToListAsync();

        var totalAnimals = animals.Count;

        var byType = animals
            .GroupBy(a => a.TypeName)
            .ToDictionary(g => g.Key, g => g.Count());

        var byStatus = animals
            .GroupBy(a => a.StatusName)
            .ToDictionary(g => g.Key, g => g.Count());

        var pregnantCount = await _db.GestationRecords
            .CountAsync(g => g.FarmId == farmId
                && g.ConfirmedDate != null
                && g.ExpectedDeliveryDate > DateTime.UtcNow
                && !g.Animal.IsDeleted);

        var sickCount = await _db.Animals
            .CountAsync(a => a.FarmId == farmId && !a.IsDeleted && a.AnimalStatus.Category == AnimalStatusCategory.Inactive);

        // ── Via existing services ──

        var degradedMetrics = new List<string>();

        // Due vaccination count
        var dueVaccinationCount = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.DueVaccinationCount, degradedMetrics, async () =>
            {
                var status = await _vaccineService.GetVaccinationStatusAsync(farmId);
                return EnsureSuccess(status, DashboardMetricNames.DueVaccinationCount, farmId, degradedMetrics)
                    ?.Count(v => v.Status == VaccinationStatusType.Due || v.Status == VaccinationStatusType.Overdue) ?? 0;
            });

        // Due weight check count
        var dueWeightCheckCount = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.DueWeightCheckCount, degradedMetrics, async () =>
            {
                var status = await _weightCheckService.GetWeightCheckStatusAsync(farmId);
                return EnsureSuccess(status, DashboardMetricNames.DueWeightCheckCount, farmId, degradedMetrics)
                    ?.Count(w => w.Status == WeightCheckStatusType.Due || w.Status == WeightCheckStatusType.Overdue) ?? 0;
            });

        // Overdue tasks
        var overdueTasksResult = await _taskService.GetTasksAsync(farmId, new Application.Tasks.FarmTaskListFilter
        {
            IncludeOverdueOnly = true,
            PageSize = 1
        });
        var overdueTasks = overdueTasksResult.IsSuccess ? overdueTasksResult.Value!.TotalCount : 0;

        // Upcoming births (gestation records due within 30 days)
        var upcomingBirths = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.UpcomingBirths, degradedMetrics, async () =>
            {
                var gestation = await _breedingService.GetGestationRecordsAsync(farmId, new GestationFilter
                {
                    ActiveOnly = true,
                    PageSize = 100
                });
                return EnsureSuccess(gestation, DashboardMetricNames.UpcomingBirths, farmId, degradedMetrics)
                    ?.Items.Count(g => g.DaysUntilDue <= 30 && g.DaysUntilDue >= 0) ?? 0;
            });

        // Feed stock value
        var feedStockResult = await _feedService.GetStockAsync(farmId);
        var totalFeedStockValue = feedStockResult.IsSuccess
            ? feedStockResult.Value!.Sum(f => f.CurrentStock > 0 ? f.CurrentStock * (f.TotalCost / Math.Max(f.QuantityPurchased, 1)) : 0)
            : 0m;

        // Inventory stock value
        var inventoryReportResult = await _inventoryService.GetReportAsync(farmId);
        var totalInventoryStockValue = inventoryReportResult.IsSuccess
            ? inventoryReportResult.Value!.TotalStockValue
            : 0m;

        var summary = new DashboardSummaryDto
        {
            TotalAnimals = totalAnimals,
            AnimalsByType = byType,
            AnimalsByStatus = byStatus,
            PregnantCount = pregnantCount,
            SickCount = sickCount,
            DueVaccinationCount = dueVaccinationCount,
            DueWeightCheckCount = dueWeightCheckCount,
            OverdueTasks = overdueTasks,
            UpcomingBirths = upcomingBirths,
            TotalFeedStockValue = totalFeedStockValue,
            TotalInventoryStockValue = totalInventoryStockValue,
            DegradedMetrics = degradedMetrics
        };

        return Result<DashboardSummaryDto>.Success(summary);
    }

    public async Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId)
    {
        var alerts = new List<DashboardAlertDto>();

        // ── Overdue vaccinations ──
        var overdueVaccinations = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.OverdueVaccinations, null, async () =>
            {
                var result = await _vaccineService.GetOverdueVaccinationsAsync(farmId);
                return EnsureSuccess(result, DashboardMetricNames.OverdueVaccinations, farmId, null);
            });
        if (overdueVaccinations is not null)
        {
            foreach (var v in overdueVaccinations)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "OverdueVaccination",
                    Severity = "Critical",
                    Title = $"Overdue: {v.VaccineTypeName}",
                    Message = $"Animal {v.AnimalTagNumber} is overdue for {v.VaccineTypeName} (due {v.NextDueDate:MMM dd, yyyy})",
                    DueDate = v.NextDueDate,
                    Link = "/dashboard/health/vaccinations"
                });
            }
        }

        // ── Overdue weight checks ──
        var overdueWeightChecks = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.OverdueWeightChecks, null, async () =>
            {
                var result = await _weightCheckService.GetOverdueWeightChecksAsync(farmId);
                return EnsureSuccess(result, DashboardMetricNames.OverdueWeightChecks, farmId, null);
            });
        if (overdueWeightChecks is not null)
        {
            foreach (var w in overdueWeightChecks)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "OverdueWeightCheck",
                    Severity = "Warning",
                    Title = $"Weight check due: {w.AnimalTagNumber}",
                    Message = $"Last recorded: {(w.LastWeightDate?.ToString("MMM dd, yyyy") ?? "Never")}. Next due: {w.NextDueDate:MMM dd, yyyy}",
                    DueDate = w.NextDueDate,
                    Link = "/dashboard/health/weight-schedules"
                });
            }
        }

        // ── Medicine alerts ──
        var medicineAlerts = await _medicineService.GetAlertsAsync(farmId);
        if (medicineAlerts.IsSuccess)
        {
            foreach (var a in medicineAlerts.Value!)
            {
                var severity = a.AlertType == MedicineAlertType.Expired ? "Critical"
                    : a.AlertType == MedicineAlertType.ExpiringSoon ? "Warning"
                    : "Info";
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "Medicine", Severity = severity,
                    Title = $"{a.AlertTypeName}: {a.MedicineName}",
                    Message = $"Batch {a.BatchNumber} — {a.CurrentQuantity} {a.Unit} remaining (threshold: {a.LowStockThreshold}). Expires {a.ExpiryDate:MMM dd, yyyy}.",
                    DueDate = a.ExpiryDate, Link = "/dashboard/health/medicines/alerts"
                });
            }
        }

        // ── Overdue tasks ──
        var overdueTasks = await _taskService.GetTasksAsync(farmId, new Application.Tasks.FarmTaskListFilter
        {
            IncludeOverdueOnly = true, PageSize = 50
        });
        if (overdueTasks.IsSuccess)
        {
            foreach (var t in overdueTasks.Value!.Items)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "OverdueTask",
                    Severity = t.Priority == FarmTaskPriority.High ? "Critical" : "Warning",
                    Title = $"Overdue: {t.Title}",
                    Message = $"Task was due {t.DueDate:MMM dd, yyyy}" +
                              (t.AssignedEmployeeName != null ? $" — assigned to {t.AssignedEmployeeName}" : ""),
                    DueDate = t.DueDate, Link = "/dashboard/tasks"
                });
            }
        }

        // ── Upcoming births ──
        var gestationItems = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.DueBirthAlerts, null, async () =>
            {
                var gestation = await _breedingService.GetGestationRecordsAsync(farmId, new GestationFilter
                {
                    ActiveOnly = true, PageSize = 100
                });
                return EnsureSuccess(gestation, DashboardMetricNames.DueBirthAlerts, farmId, null)?.Items;
            });
        if (gestationItems is not null)
        {
            foreach (var g in gestationItems.Where(g => g.DaysUntilDue <= 14 && g.DaysUntilDue >= 0))
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "DueBirth",
                    Severity = g.DaysUntilDue <= 3 ? "Critical" : "Warning",
                    Title = $"Birth due: {g.AnimalTagNumber}",
                    Message = $"Expected {g.ExpectedDeliveryDate:MMM dd, yyyy} ({g.DaysUntilDue} days). Stage: {g.CurrentStage}.",
                    DueDate = g.ExpectedDeliveryDate, Link = "/dashboard/breeding/gestation"
                });
            }
        }

        // ── Low inventory stock ──
        var inventoryReport = await _inventoryService.GetReportAsync(farmId);
        if (inventoryReport.IsSuccess)
        {
            foreach (var item in inventoryReport.Value!.LowStockAlerts)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = "LowInventory", Severity = "Warning",
                    Title = $"Low stock: {item.ItemName}",
                    Message = $"{item.Quantity} {item.Unit} remaining (reorder level: {item.ReorderLevel}). Shortfall: {item.Shortfall} {item.Unit}.",
                    Link = "/dashboard/inventory/reports"
                });
            }
        }

        var severityOrder = new Dictionary<string, int> { ["Critical"] = 0, ["Warning"] = 1, ["Info"] = 2 };
        alerts = alerts.OrderBy(a => severityOrder.GetValueOrDefault(a.Severity, 3)).ToList();
        return Result<List<DashboardAlertDto>>.Success(alerts);
    }

    public async Task<Result<DashboardChartsDto>> GetChartsAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var animalQuery = _db.Animals.Where(a => a.FarmId == farmId && !a.IsDeleted);
        if (from.HasValue) animalQuery = animalQuery.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) animalQuery = animalQuery.Where(a => a.CreatedAt <= to.Value);
        var animalTrends = await animalQuery
            .GroupBy(a => new { a.CreatedAt.Year, a.CreatedAt.Month })
            .Select(g => new AnimalTrendPoint { Month = $"{g.Key.Year}-{g.Key.Month:D2}", Count = g.Count() })
            .OrderBy(t => t.Month).ToListAsync();
        var expenseFilter = new FinanceReportFilter { From = from, To = to };
        var expenseResult = await _financeService.GetExpenseBreakdownAsync(farmId, expenseFilter);
        var feedTrendResult = await _feedService.GetConsumptionTrendAsync(farmId, "day", from, to);
        var year = (to ?? DateTime.UtcNow).Year;
        var monthlyPLResult = await _financeService.GetMonthlySummaryAsync(farmId, year);
        var charts = new DashboardChartsDto
        {
            AnimalTrends = animalTrends,
            ExpenseBreakdown = expenseResult.IsSuccess ? expenseResult.Value : null,
            FeedConsumptionTrend = feedTrendResult.IsSuccess ? feedTrendResult.Value! : new(),
            MonthlyPL = monthlyPLResult.IsSuccess ? monthlyPLResult.Value! : new()
        };
        return Result<DashboardChartsDto>.Success(charts);
    }

    // ── Metric computation with explicit degradation signaling ──

    /// <summary>
    /// Runs a metric computation, isolating EF Core provider incompatibilities
    /// (e.g. InMemory lacking a query translation) from unexpected failures.
    /// Both cases are logged with structured detail (farm ID, metric name,
    /// exception); the metric is reported as degraded (when a degradedMetrics
    /// list is supplied) and a default value is returned instead of throwing.
    /// </summary>
    private async Task<T> TryComputeMetricAsync<T>(
        Guid farmId,
        string metricName,
        List<string>? degradedMetrics,
        Func<Task<T>> compute)
    {
        try
        {
            return await compute();
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex,
                "Metric {MetricName} for farm {FarmId} is not supported by the database provider and was degraded to its default value.",
                metricName, farmId);
            degradedMetrics?.Add(metricName);
        }
        catch (NotImplementedException ex)
        {
            _logger.LogWarning(ex,
                "Metric {MetricName} for farm {FarmId} is not implemented by the database provider and was degraded to its default value.",
                metricName, farmId);
            degradedMetrics?.Add(metricName);
        }
        catch (InvalidOperationException ex)
        {
            // EF Core query-translation failures surface as InvalidOperationException.
            _logger.LogWarning(ex,
                "Metric {MetricName} for farm {FarmId} could not be translated by the database provider and was degraded to its default value.",
                metricName, farmId);
            degradedMetrics?.Add(metricName);
        }
        catch (Exception ex)
        {
            // Unexpected failure: log at Error with full structured detail.
            _logger.LogError(ex,
                "Metric {MetricName} for farm {FarmId} failed unexpectedly and was degraded to its default value.",
                metricName, farmId);
            degradedMetrics?.Add(metricName);
        }

        return default!;
    }

    /// <summary>
    /// Returns the value of a successful Result, or null after logging and
    /// marking the metric degraded (when a degradedMetrics list is supplied).
    /// </summary>
    private T? EnsureSuccess<T>(Result<T> result, string metricName, Guid farmId, List<string>? degradedMetrics) where T : class
    {
        if (result.IsSuccess)
            return result.Value;

        _logger.LogWarning(
            "Metric {MetricName} for farm {FarmId} could not be calculated: {ErrorCode}: {ErrorMessage}",
            metricName, farmId, result.Error?.Code, result.Error?.Message);
        degradedMetrics?.Add(metricName);
        return null;
    }
}
