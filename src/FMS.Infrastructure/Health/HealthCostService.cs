using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class HealthCostService : IHealthCostService
{
    private readonly FmsDbContext _context;

    public HealthCostService(FmsDbContext context)
    {
        _context = context;
    }

    public async Task<Result<HealthCostSummaryDto>> GetCostSummaryAsync(Guid farmId, HealthCostFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var medical = _context.MedicalRecords
            .Where(m => m.FarmId == farmId && !m.IsDeleted && m.Cost > 0
                && (!from.HasValue || m.DateRecorded >= from.Value)
                && (!to.HasValue || m.DateRecorded < to.Value));

        var vaccination = _context.VaccinationRecords
            .Where(vr => vr.FarmId == farmId && vr.Cost > 0
                && (!from.HasValue || vr.DateGiven >= from.Value)
                && (!to.HasValue || vr.DateGiven < to.Value));

        var medTotal = await medical.SumAsync(m => m.Cost);
        var medCount = await medical.CountAsync();
        var vaxTotal = await vaccination.SumAsync(vr => vr.Cost);
        var vaxCount = await vaccination.CountAsync();

        return Result<HealthCostSummaryDto>.Success(new HealthCostSummaryDto
        {
            TotalMedicalCost = medTotal,
            MedicalRecordCount = medCount,
            TotalVaccinationCost = vaxTotal,
            VaccinationRecordCount = vaxCount,
            GrandTotal = medTotal + vaxTotal
        });
    }

    public async Task<Result<List<HealthCostByVetDto>>> GetCostsByVetAsync(Guid farmId, HealthCostFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var medicalVets = await _context.MedicalRecords
            .Where(m => m.FarmId == farmId && !m.IsDeleted && m.Cost > 0 && m.VetName != null
                && (!from.HasValue || m.DateRecorded >= from.Value)
                && (!to.HasValue || m.DateRecorded < to.Value))
            .GroupBy(m => m.VetName!)
            .Select(g => new { VetName = g.Key, Total = g.Sum(m => m.Cost), Count = g.Count() })
            .ToListAsync();

        var vaxVets = await _context.VaccinationRecords
            .Where(vr => vr.FarmId == farmId && vr.Cost > 0 && vr.VetName != null
                && (!from.HasValue || vr.DateGiven >= from.Value)
                && (!to.HasValue || vr.DateGiven < to.Value))
            .GroupBy(vr => vr.VetName!)
            .Select(g => new { VetName = g.Key, Total = g.Sum(vr => vr.Cost), Count = g.Count() })
            .ToListAsync();

        var merged = medicalVets.Concat(vaxVets)
            .GroupBy(x => x.VetName)
            .Select(g => new HealthCostByVetDto
            {
                VetName = g.Key,
                TotalCost = g.Sum(x => x.Total),
                RecordCount = g.Sum(x => x.Count)
            })
            .OrderByDescending(x => x.TotalCost)
            .ToList();

        return Result<List<HealthCostByVetDto>>.Success(merged);
    }

    public async Task<Result<List<HealthCostByAnimalDto>>> GetCostsByAnimalAsync(Guid farmId, HealthCostFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var medicalByAnimal = await _context.MedicalRecords
            .Where(m => m.FarmId == farmId && !m.IsDeleted && m.Cost > 0
                && (!from.HasValue || m.DateRecorded >= from.Value)
                && (!to.HasValue || m.DateRecorded < to.Value))
            .GroupBy(m => new { m.AnimalId, m.Animal.TagNumber, m.Animal.Name })
            .Select(g => new { g.Key.AnimalId, g.Key.TagNumber, g.Key.Name, Total = g.Sum(m => m.Cost), Count = g.Count() })
            .ToListAsync();

        var vaxByAnimal = await _context.VaccinationRecords
            .Where(vr => vr.FarmId == farmId && vr.Cost > 0
                && (!from.HasValue || vr.DateGiven >= from.Value)
                && (!to.HasValue || vr.DateGiven < to.Value))
            .GroupBy(vr => new { vr.AnimalId, vr.Animal.TagNumber, vr.Animal.Name })
            .Select(g => new { g.Key.AnimalId, g.Key.TagNumber, g.Key.Name, Total = g.Sum(vr => vr.Cost), Count = g.Count() })
            .ToListAsync();

        var merged = medicalByAnimal.Concat(vaxByAnimal)
            .GroupBy(x => x.AnimalId)
            .Select(g => new HealthCostByAnimalDto
            {
                AnimalId = g.Key,
                TagNumber = g.First(x => x.TagNumber != null).TagNumber ?? "",
                AnimalName = g.First(x => x.Name != null).Name,
                TotalCost = g.Sum(x => x.Total),
                RecordCount = g.Sum(x => x.Count)
            })
            .OrderByDescending(x => x.TotalCost)
            .ToList();

        return Result<List<HealthCostByAnimalDto>>.Success(merged);
    }

    public async Task<Result<List<HealthCostByMonthDto>>> GetCostsByMonthAsync(Guid farmId, int year)
    {
        var start = new DateTime(year, 1, 1);
        var end = new DateTime(year + 1, 1, 1);

        var medicalByMonth = await _context.MedicalRecords
            .Where(m => m.FarmId == farmId && !m.IsDeleted && m.Cost > 0 && m.DateRecorded >= start && m.DateRecorded < end)
            .GroupBy(m => m.DateRecorded.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(m => m.Cost) })
            .ToDictionaryAsync(g => g.Month, g => g.Total);

        var vaxByMonth = await _context.VaccinationRecords
            .Where(vr => vr.FarmId == farmId && vr.Cost > 0 && vr.DateGiven >= start && vr.DateGiven < end)
            .GroupBy(vr => vr.DateGiven.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(vr => vr.Cost) })
            .ToDictionaryAsync(g => g.Month, g => g.Total);

        var months = new[] { "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December" };

        var items = Enumerable.Range(1, 12).Select(m => new HealthCostByMonthDto
        {
            Year = year,
            Month = m,
            MonthName = months[m - 1],
            MedicalCost = medicalByMonth.GetValueOrDefault(m),
            VaccinationCost = vaxByMonth.GetValueOrDefault(m),
            Total = medicalByMonth.GetValueOrDefault(m) + vaxByMonth.GetValueOrDefault(m)
        }).ToList();

        return Result<List<HealthCostByMonthDto>>.Success(items);
    }
}
