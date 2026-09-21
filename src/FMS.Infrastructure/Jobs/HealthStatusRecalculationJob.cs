using System.Diagnostics;
using FMS.Application.Health;
using FMS.Application.Jobs;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Jobs;

/// <summary>Counts produced by <see cref="HealthStatusRecalculationJob"/> for one farm.</summary>
public readonly record struct HealthStatusCounts(
    int DueVaccinations,
    int OverdueVaccinations,
    int DueWeightChecks,
    int OverdueWeightChecks);

/// <summary>
/// Recomputes a farm's due/overdue vaccination and weight-check counts and
/// stores them as a snapshot the dashboard can read without re-running the
/// per-animal status calculation on every page load.
///
/// It reuses exactly the services the dashboard's live path uses
/// (<see cref="IVaccineService.GetVaccinationStatusAsync"/> and
/// <see cref="IWeightCheckScheduleService.GetWeightCheckStatusAsync"/>), so the
/// snapshot can never disagree with a live calculation.
///
/// Intended to be invoked by Hangfire once per farm, so each farm runs in its
/// own service scope and retries independently.
/// </summary>
public class HealthStatusRecalculationJob
{
    private readonly IVaccineService _vaccineService;
    private readonly IWeightCheckScheduleService _weightCheckService;
    private readonly FmsDbContext _db;
    private readonly ILogger<HealthStatusRecalculationJob> _logger;

    public HealthStatusRecalculationJob(
        IVaccineService vaccineService,
        IWeightCheckScheduleService weightCheckService,
        FmsDbContext db,
        ILogger<HealthStatusRecalculationJob> logger)
    {
        _vaccineService = vaccineService;
        _weightCheckService = weightCheckService;
        _db = db;
        _logger = logger;
    }

    public async Task<HealthStatusCounts> ExecuteAsync(Guid farmId)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var vaccinationStatus = await _vaccineService.GetVaccinationStatusAsync(farmId);
        if (!vaccinationStatus.IsSuccess)
            throw Fail(farmId, "vaccination status", vaccinationStatus.Error?.Code, vaccinationStatus.Error?.Message);

        var weightCheckStatus = await _weightCheckService.GetWeightCheckStatusAsync(farmId);
        if (!weightCheckStatus.IsSuccess)
            throw Fail(farmId, "weight check status", weightCheckStatus.Error?.Code, weightCheckStatus.Error?.Message);

        // Due and overdue are stored separately so the snapshot can serve both
        // the dashboard counts (due + overdue, matching DashboardService's live
        // formula) and the overdue-only view.
        var counts = new HealthStatusCounts(
            DueVaccinations: vaccinationStatus.Value!.Count(v => v.Status == VaccinationStatusType.Due),
            OverdueVaccinations: vaccinationStatus.Value!.Count(v => v.Status == VaccinationStatusType.Overdue),
            DueWeightChecks: weightCheckStatus.Value!.Count(w => w.Status == WeightCheckStatusType.Due),
            OverdueWeightChecks: weightCheckStatus.Value!.Count(w => w.Status == WeightCheckStatusType.Overdue));

        var now = DateTime.UtcNow;
        var snapshot = await _db.FarmHealthStatusSnapshots
            .FirstOrDefaultAsync(s => s.FarmId == farmId);

        if (snapshot is null)
        {
            snapshot = new FarmHealthStatusSnapshot { Id = Guid.NewGuid(), FarmId = farmId, CreatedAt = now };
            _db.FarmHealthStatusSnapshots.Add(snapshot);
        }
        else
        {
            snapshot.ModifiedAt = now;
        }

        snapshot.DueVaccinationCount = counts.DueVaccinations;
        snapshot.OverdueVaccinationCount = counts.OverdueVaccinations;
        snapshot.DueWeightCheckCount = counts.DueWeightChecks;
        snapshot.OverdueWeightCheckCount = counts.OverdueWeightChecks;
        snapshot.ComputedAtUtc = now;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Job {JobName} completed for farm {FarmId} in {DurationMs}ms ({DueVaccinations} vaccinations due, " +
            "{OverdueVaccinations} overdue, {DueWeightChecks} weight checks due, {OverdueWeightChecks} overdue)",
            JobNames.HealthStatusRecalculation,
            farmId,
            Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1),
            counts.DueVaccinations,
            counts.OverdueVaccinations,
            counts.DueWeightChecks,
            counts.OverdueWeightChecks);

        return counts;
    }

    private InvalidOperationException Fail(Guid farmId, string what, string? code, string? message)
    {
        // Logged then thrown: the failure must reach the scheduler (and the job
        // status view) rather than being reduced to a zeroed snapshot.
        _logger.LogError(
            "Job {JobName} failed for farm {FarmId} while reading {Subject}: {ErrorCode} - {ErrorMessage}",
            JobNames.HealthStatusRecalculation,
            farmId,
            what,
            code,
            message);

        return new InvalidOperationException(
            $"Job {JobNames.HealthStatusRecalculation} failed for farm {farmId} while reading {what}: {code} - {message}");
    }
}
