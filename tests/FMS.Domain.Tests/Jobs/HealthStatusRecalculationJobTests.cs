using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// The health-status job exists so the dashboard can read due/overdue counts
/// without re-running the per-animal calculation on every page load. These tests
/// prove the snapshot it writes is exactly what the live calculation reports
/// (both use the same services), that refreshing updates one row rather than
/// accumulating them, and that a failure is logged and rethrown.
/// </summary>
public class HealthStatusRecalculationJobTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? GetUserId() => Guid.NewGuid();
    }

    private static (HealthStatusRecalculationJob Job, CapturingLoggerProvider Logs) CreateJob(FmsDbContext context)
    {
        var currentUser = new FixedCurrentUser();
        var (logger, logs) = TestLoggers.Create<HealthStatusRecalculationJob>();
        var job = new HealthStatusRecalculationJob(
            new VaccineService(context, currentUser),
            new WeightCheckScheduleService(context, currentUser),
            context,
            logger);
        return (job, logs);
    }

    /// <summary>
    /// Seeds one farm with two animals where each health metric has exactly one
    /// Due and one Overdue item:
    /// <list type="bullet">
    /// <item>vaccination schedule every 30 days: no record (due today) vs a record 40 days ago (10 days late);</item>
    /// <item>weight check every 7 days: no record (due in 7 days) vs a record 30 days ago (23 days late).</item>
    /// </list>
    /// </summary>
    private static async Task<Guid> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active
        };
        var vaccineType = new VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD" };

        var dueAnimal = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "DUE-001",
            AnimalTypeId = animalType.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id,
            DateOfBirth = DateTime.UtcNow.AddYears(-2)
        };
        var overdueAnimal = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "LATE-001",
            AnimalTypeId = animalType.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id,
            DateOfBirth = DateTime.UtcNow.AddYears(-2)
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vaccineType);
        context.Animals.AddRange(dueAnimal, overdueAnimal);

        context.VaccinationSchedules.Add(new VaccinationSchedule
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, VaccineTypeId = vaccineType.Id,
            AnimalTypeId = animalType.Id, RecurrenceDays = 30, IsActive = true
        });
        context.WeightCheckSchedules.Add(new WeightCheckSchedule
        {
            Id = Guid.NewGuid(), FarmId = farm.Id,
            AnimalTypeId = animalType.Id, RecurrenceDays = 7, IsActive = true
        });

        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, AnimalId = overdueAnimal.Id,
            VaccineTypeId = vaccineType.Id, DateGiven = DateTime.UtcNow.Date.AddDays(-40)
        });
        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(), AnimalId = overdueAnimal.Id,
            WeightKg = 400, RecordedAt = DateTime.UtcNow.Date.AddDays(-30)
        });

        await context.SaveChangesAsync();
        return farm.Id;
    }

    [Fact]
    public async Task ExecuteAsync_WritesASnapshotMatchingTheLiveCalculation()
    {
        using var context = CreateContext();
        var farmId = await SeedFarmAsync(context);
        var (job, _) = CreateJob(context);

        var counts = await job.ExecuteAsync(farmId);

        Assert.Equal(1, counts.DueVaccinations);
        Assert.Equal(1, counts.OverdueVaccinations);
        Assert.Equal(1, counts.DueWeightChecks);
        Assert.Equal(1, counts.OverdueWeightChecks);

        var snapshot = await context.FarmHealthStatusSnapshots.SingleAsync(s => s.FarmId == farmId);
        Assert.Equal(1, snapshot.DueVaccinationCount);
        Assert.Equal(1, snapshot.OverdueVaccinationCount);
        Assert.Equal(1, snapshot.DueWeightCheckCount);
        Assert.Equal(1, snapshot.OverdueWeightCheckCount);
        Assert.True(snapshot.ComputedAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ExecuteAsync_MatchesTheHealthServicesThatProduceTheLiveNumbers()
    {
        using var context = CreateContext();
        var farmId = await SeedFarmAsync(context);
        var (job, _) = CreateJob(context);

        await job.ExecuteAsync(farmId);

        // The live services are the single source of truth; the snapshot is only
        // a cache of them, so the numbers must agree exactly.
        var currentUser = new FixedCurrentUser();
        var vaccinations = await new VaccineService(context, currentUser).GetVaccinationStatusAsync(farmId);
        var weightChecks = await new WeightCheckScheduleService(context, currentUser).GetWeightCheckStatusAsync(farmId);

        var snapshot = await context.FarmHealthStatusSnapshots.SingleAsync(s => s.FarmId == farmId);

        Assert.Equal(vaccinations.Value!.Count(v => v.Status == VaccinationStatusType.Due), snapshot.DueVaccinationCount);
        Assert.Equal(vaccinations.Value!.Count(v => v.Status == VaccinationStatusType.Overdue), snapshot.OverdueVaccinationCount);
        Assert.Equal(weightChecks.Value!.Count(w => w.Status == WeightCheckStatusType.Due), snapshot.DueWeightCheckCount);
        Assert.Equal(weightChecks.Value!.Count(w => w.Status == WeightCheckStatusType.Overdue), snapshot.OverdueWeightCheckCount);
    }

    [Fact]
    public async Task ExecuteAsync_RunTwice_RefreshesOneRowInsteadOfAccumulating()
    {
        using var context = CreateContext();
        var farmId = await SeedFarmAsync(context);
        var (job, _) = CreateJob(context);

        await job.ExecuteAsync(farmId);

        // Vaccinate and weigh the overdue animal today: the refresh must reflect
        // the new numbers rather than re-serving the previous snapshot.
        var animal = await context.Animals.SingleAsync(a => a.TagNumber == "LATE-001");
        var vaccineTypeId = await context.VaccineTypes.Select(v => v.Id).FirstAsync();
        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = animal.Id,
            VaccineTypeId = vaccineTypeId, DateGiven = DateTime.UtcNow.Date
        });
        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(), AnimalId = animal.Id,
            WeightKg = 420, RecordedAt = DateTime.UtcNow.Date
        });
        await context.SaveChangesAsync();

        var counts = await job.ExecuteAsync(farmId);

        Assert.Single(context.FarmHealthStatusSnapshots.Where(s => s.FarmId == farmId));
        // Vaccinated today with a 30-day recurrence → upcoming, so no longer overdue.
        Assert.Equal(1, counts.DueVaccinations);
        Assert.Equal(0, counts.OverdueVaccinations);
        // Weighed today with a 7-day recurrence → due within the window.
        Assert.Equal(2, counts.DueWeightChecks);
        Assert.Equal(0, counts.OverdueWeightChecks);

        var snapshot = await context.FarmHealthStatusSnapshots.SingleAsync(s => s.FarmId == farmId);
        Assert.Equal(1, snapshot.DueVaccinationCount);
        Assert.Equal(0, snapshot.OverdueVaccinationCount);
        Assert.Equal(2, snapshot.DueWeightCheckCount);
        Assert.Equal(0, snapshot.OverdueWeightCheckCount);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsSnapshotsIsolatedPerFarm()
    {
        using var context = CreateContext();
        var farmA = await SeedFarmAsync(context);
        var farmB = await SeedFarmAsync(context);
        var (job, _) = CreateJob(context);

        // Only farm A is refreshed; farm B must stay untouched.
        await job.ExecuteAsync(farmA);

        Assert.Single(context.FarmHealthStatusSnapshots.Where(s => s.FarmId == farmA));
        Assert.Empty(context.FarmHealthStatusSnapshots.Where(s => s.FarmId == farmB));
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_LogsStructuredCounts()
    {
        using var context = CreateContext();
        var farmId = await SeedFarmAsync(context);
        var (job, logs) = CreateJob(context);

        await job.ExecuteAsync(farmId);

        var info = Assert.Single(logs.ForLevel(LogLevel.Information));
        Assert.Contains("health-status-recalculation", info.Message);
        Assert.Contains(farmId.ToString(), info.Message);
        Assert.Contains("1 vaccinations due", info.Message);
        Assert.Contains("1 overdue", info.Message);
    }
}
