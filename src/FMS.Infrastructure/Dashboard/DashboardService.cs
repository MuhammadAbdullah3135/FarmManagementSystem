using FMS.Application.Common;
using FMS.Application.Dashboard;
using FMS.Application.Feed;
using FMS.Application.Finance;
using FMS.Application.Health;
using FMS.Application.Inventory;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Application.Tasks;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IOptions<JobOptions> _jobOptions;

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
        ILogger<DashboardService> logger,
        IOptions<JobOptions> jobOptions)
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
        _jobOptions = jobOptions;
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

        // Due/overdue health counts. The background health-status job keeps a
        // per-farm snapshot, read here as a fast path over the per-animal status
        // calculation below (the most expensive part of this method).
        //
        // A missing or stale snapshot falls back to that live calculation, so a
        // stopped job degrades *freshness*, never correctness — which is why
        // neither case is reported as a degraded metric here. (DegradedMetrics
        // means "this value is a default, do not trust it"; the fallback value
        // is a real count.) Staleness is logged, and surfaced to operators by
        // the job-status view.
        var healthSnapshot = await TryGetFreshHealthSnapshotAsync(farmId);

        DateTime? healthSnapshotComputedAtUtc = null;
        int dueVaccinationCount;
        int dueWeightCheckCount;

        if (healthSnapshot is not null)
        {
            // The snapshot stores Due and Overdue separately, so both the
            // dashboard count (due + overdue) and an overdue-only view can be
            // served from it.
            dueVaccinationCount = healthSnapshot.DueVaccinationCount + healthSnapshot.OverdueVaccinationCount;
            dueWeightCheckCount = healthSnapshot.DueWeightCheckCount + healthSnapshot.OverdueWeightCheckCount;
            healthSnapshotComputedAtUtc = healthSnapshot.ComputedAtUtc;
        }
        else
        {
            // Due vaccination count
            dueVaccinationCount = await TryComputeMetricAsync(
                farmId, DashboardMetricNames.DueVaccinationCount, degradedMetrics, async () =>
                {
                    var status = await _vaccineService.GetVaccinationStatusAsync(farmId);
                    return EnsureSuccess(status, DashboardMetricNames.DueVaccinationCount, farmId, degradedMetrics)
                        ?.Count(v => v.Status == VaccinationStatusType.Due || v.Status == VaccinationStatusType.Overdue) ?? 0;
                });

            // Due weight check count
            dueWeightCheckCount = await TryComputeMetricAsync(
                farmId, DashboardMetricNames.DueWeightCheckCount, degradedMetrics, async () =>
                {
                    var status = await _weightCheckService.GetWeightCheckStatusAsync(farmId);
                    return EnsureSuccess(status, DashboardMetricNames.DueWeightCheckCount, farmId, degradedMetrics)
                        ?.Count(w => w.Status == WeightCheckStatusType.Due || w.Status == WeightCheckStatusType.Overdue) ?? 0;
                });
        }

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
            HealthSnapshotComputedAtUtc = healthSnapshotComputedAtUtc,
            DegradedMetrics = degradedMetrics
        };

        return Result<DashboardSummaryDto>.Success(summary);
    }

    public async Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId)
    {
        var result = await ComputeAlertSetAsync(farmId);

        return result.IsSuccess
            ? Result<List<DashboardAlertDto>>.Success(result.Value!.Alerts)
            : Result<List<DashboardAlertDto>>.Failure(result.Error!);
    }

    /// <inheritdoc />
    public async Task<Result<DashboardAlertSetDto>> ComputeAlertSetAsync(Guid farmId)
    {
        var alerts = new List<DashboardAlertDto>();

        // Alert types whose inputs failed to compute. Only the notification
        // dispatcher consumes this (the dashboard endpoint drops it), and it is
        // the difference between "no overdue vaccines" and "we could not tell".
        var unavailableAlertTypes = new List<string>();

        // ── Overdue vaccinations ──
        var overdueVaccinations = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.OverdueVaccinations, unavailableAlertTypes, async () =>
            {
                var result = await _vaccineService.GetOverdueVaccinationsAsync(farmId);
                return EnsureSuccess(result, DashboardMetricNames.OverdueVaccinations, farmId, unavailableAlertTypes);
            });
        if (overdueVaccinations is not null)
        {
            foreach (var v in overdueVaccinations)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.OverdueVaccination,
                    Severity = NotificationSeverity.Critical,
                    Title = $"Overdue: {v.VaccineTypeName}",
                    Message = $"Animal {v.AnimalTagNumber} is overdue for {v.VaccineTypeName} (due {v.NextDueDate:MMM dd, yyyy})",
                    DueDate = v.NextDueDate,
                    Link = "/dashboard/health/vaccinations",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForOverdueVaccination(v.AnimalId, v.VaccineTypeId)
                });
            }
        }

        // ── Overdue weight checks ──
        var overdueWeightChecks = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.OverdueWeightChecks, unavailableAlertTypes, async () =>
            {
                var result = await _weightCheckService.GetOverdueWeightChecksAsync(farmId);
                return EnsureSuccess(result, DashboardMetricNames.OverdueWeightChecks, farmId, unavailableAlertTypes);
            });
        if (overdueWeightChecks is not null)
        {
            foreach (var w in overdueWeightChecks)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.OverdueWeightCheck,
                    Severity = NotificationSeverity.Warning,
                    Title = $"Weight check due: {w.AnimalTagNumber}",
                    Message = $"Last recorded: {(w.LastWeightDate?.ToString("MMM dd, yyyy") ?? "Never")}. Next due: {w.NextDueDate:MMM dd, yyyy}",
                    DueDate = w.NextDueDate,
                    Link = "/dashboard/health/weight-schedules",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForOverdueWeightCheck(w.AnimalId)
                });
            }
        }

        // ── Medicine alerts ──
        // Routed through EnsureSuccess so a failed lookup is recorded against the
        // alert type instead of reading as "no medicine alerts at all".
        var medicineAlerts = EnsureSuccess(
            await _medicineService.GetAlertsAsync(farmId),
            DashboardMetricNames.MedicineAlerts, farmId, unavailableAlertTypes);
        if (medicineAlerts is not null)
        {
            foreach (var a in medicineAlerts)
            {
                var severity = a.AlertType == MedicineAlertType.Expired ? NotificationSeverity.Critical
                    : a.AlertType == MedicineAlertType.ExpiringSoon ? NotificationSeverity.Warning
                    : NotificationSeverity.Info;
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.Medicine, Severity = severity,
                    Title = $"{a.AlertTypeName}: {a.MedicineName}",
                    Message = $"Batch {a.BatchNumber} — {a.CurrentQuantity} {a.Unit} remaining (threshold: {a.LowStockThreshold}). Expires {a.ExpiryDate:MMM dd, yyyy}.",
                    DueDate = a.ExpiryDate, Link = "/dashboard/health/medicines/alerts",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForMedicine(a.MedicineId, a.StockId, a.AlertType.ToString())
                });
            }
        }

        // ── Overdue tasks ──
        var overdueTasks = EnsureSuccess(
            await _taskService.GetTasksAsync(farmId, new Application.Tasks.FarmTaskListFilter
            {
                IncludeOverdueOnly = true, PageSize = 50
            }),
            DashboardMetricNames.OverdueTasks, farmId, unavailableAlertTypes);
        if (overdueTasks is not null)
        {
            foreach (var t in overdueTasks.Items)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.OverdueTask,
                    Severity = t.Priority == FarmTaskPriority.High ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    Title = $"Overdue: {t.Title}",
                    Message = $"Task was due {t.DueDate:MMM dd, yyyy}" +
                              (t.AssignedEmployeeName != null ? $" — assigned to {t.AssignedEmployeeName}" : ""),
                    DueDate = t.DueDate, Link = "/dashboard/tasks",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForOverdueTask(t.Id)
                });
            }
        }

        // ── Upcoming births ──
        var gestationItems = await TryComputeMetricAsync(
            farmId, DashboardMetricNames.DueBirthAlerts, unavailableAlertTypes, async () =>
            {
                var gestation = await _breedingService.GetGestationRecordsAsync(farmId, new GestationFilter
                {
                    ActiveOnly = true, PageSize = 100
                });
                return EnsureSuccess(gestation, DashboardMetricNames.DueBirthAlerts, farmId, unavailableAlertTypes)?.Items;
            });
        if (gestationItems is not null)
        {
            foreach (var g in gestationItems.Where(g => g.DaysUntilDue <= 14 && g.DaysUntilDue >= 0))
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.DueBirth,
                    Severity = g.DaysUntilDue <= 3 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    Title = $"Birth due: {g.AnimalTagNumber}",
                    Message = $"Expected {g.ExpectedDeliveryDate:MMM dd, yyyy} ({g.DaysUntilDue} days). Stage: {g.CurrentStage}.",
                    DueDate = g.ExpectedDeliveryDate, Link = "/dashboard/breeding/gestation",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForDueBirth(g.Id)
                });
            }
        }

        // ── Low inventory stock ──
        var inventoryReport = EnsureSuccess(
            await _inventoryService.GetReportAsync(farmId),
            DashboardMetricNames.LowInventory, farmId, unavailableAlertTypes);
        if (inventoryReport is not null)
        {
            foreach (var item in inventoryReport.LowStockAlerts)
            {
                alerts.Add(new DashboardAlertDto
                {
                    AlertType = NotificationAlertTypes.LowInventory, Severity = NotificationSeverity.Warning,
                    Title = $"Low stock: {item.ItemName}",
                    Message = $"{item.Quantity} {item.Unit} remaining (reorder level: {item.ReorderLevel}). Shortfall: {item.Shortfall} {item.Unit}.",
                    Link = "/dashboard/inventory/reports",
                    SourceKey = NotificationAlertTypes.SourceKeys.ForLowInventory(item.InventoryItemId)
                });
            }
        }

        alerts = alerts.OrderBy(a => NotificationSeverity.Rank(a.Severity)).ToList();

        var unavailableAlertTypeNames = unavailableAlertTypes
            .SelectMany(NotificationAlertTypes.AlertTypesGatedByMetric)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result<DashboardAlertSetDto>.Success(new DashboardAlertSetDto
        {
            Alerts = alerts,
            UnavailableAlertTypes = unavailableAlertTypeNames
        });
    }

    /// <summary>
    /// The animal-additions trend as a query the database runs, one row per month.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetChartsAsync"/> so it can be asserted directly against the
    /// Npgsql provider without a database (<c>NpgsqlQueryTranslationTests</c>). This is the
    /// shape that works: group by the two date parts and order by them, with no computed label
    /// in the statement. The version that used to live here projected a formatted
    /// "{year}-{month}" string and then ordered by it, which PostgreSQL cannot translate — the
    /// query threw and every dashboard chart rendered "No data" behind a 500, while the InMemory
    /// provider the suite runs on returned rows and kept the tests green.
    /// </remarks>
    public static IQueryable<AnimalTrendBucket> BuildAnimalTrendBuckets(
        FmsDbContext db, Guid farmId, DateTime? from, DateTime? to)
    {
        var query = db.Animals.Where(a => a.FarmId == farmId && !a.IsDeleted);
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAt <= to.Value);

        return query
            .GroupBy(a => new { a.CreatedAt.Year, a.CreatedAt.Month })
            .Select(g => new AnimalTrendBucket { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count() });
    }

    public async Task<Result<DashboardChartsDto>> GetChartsAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        // Grouping, counting and ordering happen in SQL; the "{year}-{month}" label is applied
        // to the returned rows afterwards, because ordering by a computed label is exactly what
        // Npgsql refuses to translate (see BuildAnimalTrendBuckets).
        var animalTrendBuckets = await BuildAnimalTrendBuckets(_db, farmId, from, to).ToListAsync();

        var animalTrends = animalTrendBuckets
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .Select(b => new AnimalTrendPoint { Month = $"{b.Year}-{b.Month:D2}", Count = b.Count })
            .ToList();
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

    /// <summary>
    /// Reads the farm's health-status snapshot (written by the
    /// <c>health-status-recalculation</c> job) when it exists and is within the
    /// configured staleness window. Returns null when it is missing, stale, or
    /// unreadable — the caller must then fall back to a live calculation.
    /// </summary>
    private async Task<FarmHealthStatusSnapshot?> TryGetFreshHealthSnapshotAsync(Guid farmId)
    {
        var stalenessMinutes = Math.Max(1, _jobOptions.Value.HealthSnapshot.StalenessMinutes);

        // A read failure here must never break the summary: it just means "no
        // fast path available", which is exactly what null means.
        var snapshot = await TryComputeMetricAsync<FarmHealthStatusSnapshot?>(
            farmId,
            "HealthStatusSnapshot",
            null,
            () => _db.FarmHealthStatusSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.FarmId == farmId));

        if (snapshot is null)
            return null;

        var age = DateTime.UtcNow - snapshot.ComputedAtUtc;
        if (age <= TimeSpan.FromMinutes(stalenessMinutes))
            return snapshot;

        _logger.LogWarning(
            "Health status snapshot for farm {FarmId} is stale (computed {AgeMinutes} minutes ago, staleness limit {StalenessMinutes} minutes); "
            + "falling back to live calculation. Job {JobName} may not be running.",
            farmId,
            Math.Round(age.TotalMinutes, 1),
            stalenessMinutes,
            JobNames.HealthStatusRecalculation);

        return null;
    }
}
