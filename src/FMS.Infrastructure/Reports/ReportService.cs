using FMS.Application.Common;
using FMS.Application.Reports;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly FmsDbContext _db;

    public ReportService(FmsDbContext db)
    {
        _db = db;
    }

    public async Task<Result<AnimalReportDto>> GetAnimalReportAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var query = _db.Animals
            .Where(a => a.FarmId == farmId && !a.IsDeleted);

        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value);

        var animals = await query
            .Select(a => new
            {
                TypeName = a.AnimalType.Name,
                StatusName = a.AnimalStatus.Name,
                Category = a.AnimalStatus.Category,
                a.CreatedAt,
                a.IsDeleted
            })
            .ToListAsync();

        var totalCount = animals.Count;

        var byType = animals
            .GroupBy(a => a.TypeName)
            .Select(g => new AnimalCountByCategory { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var byStatus = animals
            .GroupBy(a => a.StatusName)
            .Select(g => new AnimalCountByCategory { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        // Growth trend — average weight per month from WeightRecords
        var weightQuery = _db.WeightRecords
            .Where(w => w.Animal.FarmId == farmId && !w.Animal.IsDeleted);

        if (from.HasValue)
            weightQuery = weightQuery.Where(w => w.RecordedAt >= from.Value);
        if (to.HasValue)
            weightQuery = weightQuery.Where(w => w.RecordedAt <= to.Value);

        // The scalars are loaded and grouped in memory, the way the sibling reports below do it.
        // The previous version projected the trend straight out of SQL: a formatted
        // "{year}-{month}" label in the projection and an OrderBy on that label, which
        // PostgreSQL cannot translate (ordering by a value the same statement computes). That
        // threw, so the whole animal report answered 500 — invisible to the suite, which runs on
        // the InMemory provider that evaluates this client-side.
        var weightRows = await weightQuery
            .Select(w => new { w.RecordedAt, w.WeightKg, w.AnimalId })
            .ToListAsync();

        var growthTrend = weightRows
            .GroupBy(w => new { w.RecordedAt.Year, w.RecordedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new AnimalTrendPointDto
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                AvgWeight = g.Average(w => w.WeightKg),
                AnimalCount = g.Select(w => w.AnimalId).Distinct().Count()
            })
            .ToList();

        // Mortality — animals with Terminal status
        var mortalityCount = await _db.Animals
            .CountAsync(a => a.FarmId == farmId && a.AnimalStatus.Category == AnimalStatusCategory.Terminal && !a.IsDeleted);

        // Transfers
        var transferQuery = _db.AnimalTransfers
            .Where(t => t.FarmId == farmId);

        if (from.HasValue)
            transferQuery = transferQuery.Where(t => t.TransferredAt >= from.Value);
        if (to.HasValue)
            transferQuery = transferQuery.Where(t => t.TransferredAt <= to.Value);

        var transferCount = await transferQuery.CountAsync();

        var report = new AnimalReportDto
        {
            TotalCount = totalCount,
            ByType = byType,
            ByStatus = byStatus,
            GrowthTrend = growthTrend,
            MortalityCount = mortalityCount,
            TransferCount = transferCount
        };

        return Result<AnimalReportDto>.Success(report);
    }

    public async Task<Result<MedicalReportDto>> GetMedicalReportAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var query = _db.MedicalRecords
            .Where(m => m.FarmId == farmId && !m.IsDeleted);

        if (from.HasValue)
            query = query.Where(m => m.DateRecorded >= from.Value);
        if (to.HasValue)
            query = query.Where(m => m.DateRecorded <= to.Value);

        var records = await query
            .Select(m => new
            {
                m.DateRecorded,
                m.Cost,
                m.VetName,
                m.Status
            })
            .ToListAsync();

        var totalCases = records.Count;
        var totalCost = records.Sum(r => r.Cost);

        var monthlyTrend = records
            .GroupBy(r => new { r.DateRecorded.Year, r.DateRecorded.Month })
            .Select(g => new MedicalMonthlyTrend
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                CaseCount = g.Count(),
                Cost = g.Sum(r => r.Cost)
            })
            .OrderBy(t => t.Month)
            .ToList();

        var byVet = records
            .Where(r => !string.IsNullOrEmpty(r.VetName))
            .GroupBy(r => r.VetName!)
            .Select(g => new MedicalByVet
            {
                VetName = g.Key,
                CaseCount = g.Count(),
                TotalCost = g.Sum(r => r.Cost)
            })
            .OrderByDescending(v => v.TotalCost)
            .ToList();

        var byStatus = records
            .GroupBy(r => r.Status.ToString())
            .Select(g => new MedicalByStatus
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToList();

        var report = new MedicalReportDto
        {
            TotalCases = totalCases,
            TotalCost = totalCost,
            MonthlyTrend = monthlyTrend,
            ByVet = byVet,
            ByStatus = byStatus
        };

        return Result<MedicalReportDto>.Success(report);
    }

    public async Task<Result<VaccinationReportDto>> GetVaccinationReportAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var query = _db.VaccinationRecords
            .Where(v => v.FarmId == farmId);

        if (from.HasValue)
            query = query.Where(v => v.DateGiven >= from.Value);
        if (to.HasValue)
            query = query.Where(v => v.DateGiven <= to.Value);

        var records = await query
            .Select(v => new
            {
                v.DateGiven,
                v.Cost,
                v.VaccineType.Name
            })
            .ToListAsync();

        var totalVaccinations = records.Count;
        var totalCost = records.Sum(r => r.Cost);

        var monthlyTrend = records
            .GroupBy(r => new { r.DateGiven.Year, r.DateGiven.Month })
            .Select(g => new VaccinationMonthlyTrend
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                Count = g.Count(),
                Cost = g.Sum(r => r.Cost)
            })
            .OrderBy(t => t.Month)
            .ToList();

        var byVaccine = records
            .GroupBy(r => r.Name)
            .Select(g => new VaccinationByVaccine
            {
                VaccineName = g.Key,
                Count = g.Count(),
                TotalCost = g.Sum(r => r.Cost)
            })
            .OrderByDescending(v => v.Count)
            .ToList();

        var today = DateTime.UtcNow.Date;
        var schedules = await _db.VaccinationSchedules
            .Where(s => s.FarmId == farmId && s.IsActive)
            .ToListAsync();
        var animals = await _db.Animals
            .Where(a => a.FarmId == farmId && !a.IsDeleted)
            .ToListAsync();

        // Due/upcoming is worked out from one load of the farm's vaccination history instead of
        // a query per (schedule × animal) pair — the shape that made this report cost thousands
        // of round trips on a farm with real history. The map holds, per pair, the same value the
        // old OrderByDescending/First asked for one pair at a time: the latest date given.
        //
        // Deliberately NOT reused from the range-filtered query above. "When was this animal
        // last vaccinated" is a question about all time, while that query is bounded by the
        // report's dates; deriving one from the other would silently change the counts whenever
        // a date range is supplied.
        var latestVaccinationByAnimalAndVaccine = (await _db.VaccinationRecords
                .AsNoTracking()
                .Where(v => v.Animal.FarmId == farmId && !v.Animal.IsDeleted)
                .Select(v => new { v.AnimalId, v.VaccineTypeId, v.DateGiven })
                .ToListAsync())
            .GroupBy(v => (v.AnimalId, v.VaccineTypeId))
            .ToDictionary(g => g.Key, g => g.Max(v => v.DateGiven));

        var overdueCount = 0;
        var upcomingCount = 0;
        foreach (var schedule in schedules)
        {
            foreach (var animal in animals.Where(a => (!schedule.AnimalTypeId.HasValue || a.AnimalTypeId == schedule.AnimalTypeId) && (!schedule.BreedId.HasValue || a.BreedId == schedule.BreedId)))
            {
                var lastDate = latestVaccinationByAnimalAndVaccine
                    .TryGetValue((animal.Id, schedule.VaccineTypeId), out var lastGivenAt)
                        ? lastGivenAt
                        : (DateTime?)null;

                var dueDate = lastDate?.AddDays(schedule.RecurrenceDays).Date ?? today;
                if (dueDate < today) overdueCount++; else upcomingCount++;
            }
        }

        var report = new VaccinationReportDto
        {
            TotalVaccinations = totalVaccinations,
            TotalCost = totalCost,
            OverdueCount = overdueCount,
            UpcomingCount = upcomingCount,
            MonthlyTrend = monthlyTrend,
            ByVaccine = byVaccine
        };

        return Result<VaccinationReportDto>.Success(report);
    }

    public async Task<Result<EmployeeReportDto>> GetEmployeeReportAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var employees = await _db.Employees
            .Where(e => e.FarmId == farmId && !e.IsDeleted)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.IsActive, DeptName = e.Department!.Name })
            .ToListAsync();

        var totalEmployees = employees.Count;
        var activeEmployees = employees.Count(e => e.IsActive);

        var paymentQuery = _db.SalaryPayments
            .Where(p => p.Employee.FarmId == farmId && !p.Employee.IsDeleted);

        if (from.HasValue)
            paymentQuery = paymentQuery.Where(p => p.PaymentDate >= from.Value);
        if (to.HasValue)
            paymentQuery = paymentQuery.Where(p => p.PaymentDate <= to.Value);

        var payments = await paymentQuery
            .Select(p => new
            {
                p.Amount,
                p.PaymentDate,
                p.EmployeeId,
                EmployeeName = p.Employee.FirstName + " " + p.Employee.LastName,
                DeptName = p.Employee.Department!.Name
            })
            .ToListAsync();

        var totalPaid = payments.Sum(p => p.Amount);
        var paymentCount = payments.Count;

        var byMonth = payments
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new EmployeePayrollByMonth
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                Amount = g.Sum(p => p.Amount),
                PaymentCount = g.Count()
            })
            .OrderBy(m => m.Month)
            .ToList();

        var byDept = payments
            .GroupBy(p => p.DeptName ?? "Unassigned")
            .Select(g => new EmployeePayrollByDepartment
            {
                DepartmentName = g.Key,
                EmployeeCount = g.Select(p => p.EmployeeId).Distinct().Count(),
                TotalPaid = g.Sum(p => p.Amount)
            })
            .OrderByDescending(d => d.TotalPaid)
            .ToList();

        var report = new EmployeeReportDto
        {
            TotalEmployees = totalEmployees,
            ActiveEmployees = activeEmployees,
            TotalPaid = totalPaid,
            PaymentCount = paymentCount,
            ExpectedMonthlyPayroll = 0, // computed separately if needed
            ByMonth = byMonth,
            ByDepartment = byDept
        };

        return Result<EmployeeReportDto>.Success(report);
    }
}
