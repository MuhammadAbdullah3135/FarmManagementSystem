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

    public DashboardService(
        FmsDbContext db,
        IVaccineService vaccineService,
        IMedicineService medicineService,
        IFarmTaskService taskService,
        IBreedingSvc breedingService,
        IInventoryService inventoryService,
        IFeedService feedService,
        IFinanceService financeService,
        IWeightCheckScheduleService weightCheckService)
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
    }

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid farmId)
    {
        var today = DateTime.UtcNow.Date;

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

        // Due vaccination count (may fail on InMemory due to DateDiffDay)
        int dueVaccinationCount = 0;
        try
        {
            var vaccinationStatus = await _vaccineService.GetVaccinationStatusAsync(farmId);
            dueVaccinationCount = vaccinationStatus.IsSuccess
                ? vaccinationStatus.Value!.Count(v => v.Status == VaccinationStatusType.Due || v.Status == VaccinationStatusType.Overdue)
                : 0;
        }
        catch { /* InMemory DB doesn't support DateDiffDay */ }

        // Due weight check count
        int dueWeightCheckCount = 0;
        try
        {
            var weightCheckStatus = await _weightCheckService.GetWeightCheckStatusAsync(farmId);
            dueWeightCheckCount = weightCheckStatus.IsSuccess
                ? weightCheckStatus.Value!.Count(w => w.Status == WeightCheckStatusType.Due || w.Status == WeightCheckStatusType.Overdue)
                : 0;
        }
        catch { /* InMemory DB compat */ }

        // Overdue tasks
        var overdueTasksResult = await _taskService.GetTasksAsync(farmId, new Application.Tasks.FarmTaskListFilter
        {
            IncludeOverdueOnly = true,
            PageSize = 1
        });
        var overdueTasks = overdueTasksResult.IsSuccess ? overdueTasksResult.Value!.TotalCount : 0;

        // Upcoming births (gestation records due within 30 days — may fail on InMemory due to DateDiffDay)
        int upcomingBirths = 0;
        try
        {
            var gestationResult = await _breedingService.GetGestationRecordsAsync(farmId, new GestationFilter
            {
                ActiveOnly = true,
                PageSize = 100
            });
            upcomingBirths = gestationResult.IsSuccess
                ? gestationResult.Value!.Items.Count(g => g.DaysUntilDue <= 30 && g.DaysUntilDue >= 0)
                : 0;
        }
        catch { /* InMemory DB doesn't support DateDiffDay */ }

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
            TotalInventoryStockValue = totalInventoryStockValue
        };

        return Result<DashboardSummaryDto>.Success(summary);
    }

    public async Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId)
    {
        var alerts = new List<DashboardAlertDto>();

        // ── Overdue vaccinations (may fail on InMemory due to DateDiffDay) ──
        try
        {
            var overdueVaccResult = await _vaccineService.GetOverdueVaccinationsAsync(farmId);
            if (overdueVaccResult.IsSuccess)
            {
                foreach (var v in overdueVaccResult.Value!)
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
        }
        catch { /* InMemory DB doesn't support DateDiffDay */ }

        // ── Overdue weight checks ──
        try
        {
            var overdueWeightResult = await _weightCheckService.GetOverdueWeightChecksAsync(farmId);
            if (overdueWeightResult.IsSuccess)
            {
                foreach (var w in overdueWeightResult.Value!)
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
        }
        catch { /* InMemory DB compat */ }

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
        try
        {
            var gestationResult = await _breedingService.GetGestationRecordsAsync(farmId, new GestationFilter
            {
                ActiveOnly = true, PageSize = 100
            });
            if (gestationResult.IsSuccess)
            {
                foreach (var g in gestationResult.Value!.Items.Where(g => g.DaysUntilDue <= 14 && g.DaysUntilDue >= 0))
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
        }
        catch { }

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
}
