using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// Three health paths answer "which animals?" differently for a soft-deleted animal, and this
/// pins each answer so the difference is a decision rather than drift.
///
/// <para>
/// The vaccination report and the weight-check path exclude deleted animals; the vaccination
/// status — which feeds the dashboard counts and the notification dispatcher — includes them.
/// That is inconsistent on its face, and the Phase 6.3 rewrite deliberately preserved it: these
/// values reach the dashboard and dispatched notifications, so changing them changes what a user
/// is told about their farm, which is its own decision with its own evidence.
/// </para>
///
/// <para>
/// It is pinned here because nothing else did. The rewrite's golden outputs are seeded with live
/// animals only, so they cannot see this at all, and no other test exercised a deleted animal
/// with vaccination history — meaning the behaviour was preserved only by luck of not having been
/// edited, and a later change to either filter would have gone unnoticed.
/// </para>
/// </summary>
public class SoftDeletedAnimalScopeTests
{
    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    [Fact]
    public async Task ASoftDeletedAnimal_IsCountedByTheStatusPath_AndExcludedByTheReportAndWeightCheck()
    {
        using var counter = new SqliteQueryCounter();
        var db = counter.Context;

        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        db.Accounts.Add(new Account { Id = accountId, Name = "Scope account" });
        db.Farms.Add(new Farm { Id = farmId, AccountId = accountId, Name = "Scope farm" });

        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cattle" };
        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            AnimalType = animalType,
            AnimalTypeId = animalType.Id,
            Name = "Holstein"
        };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farmId, Value = "Female" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Active",
            Category = AnimalStatusCategory.Active
        };
        var ageCategory = new AgeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Adult",
            MinDays = 365,
            MaxDays = 9_999
        };
        var vaccineType = new VaccineType { Id = Guid.NewGuid(), FarmId = farmId, Name = "FMD Vaccine" };

        var live = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            TagNumber = "LIVE-1",
            Name = "Living",
            AnimalTypeId = animalType.Id,
            BreedId = breed.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id,
            AgeCategoryId = ageCategory.Id,
            DateOfBirth = DateTime.UtcNow.AddYears(-3)
        };

        // The tombstone. Both animals carry the same history, so the only thing that can separate
        // them in any answer is the IsDeleted filter itself.
        var deleted = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            TagNumber = "GONE-1",
            Name = "Departed",
            AnimalTypeId = animalType.Id,
            BreedId = breed.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id,
            AgeCategoryId = ageCategory.Id,
            DateOfBirth = DateTime.UtcNow.AddYears(-3),
            IsDeleted = true
        };

        db.AddRange(animalType, breed, sex, status, ageCategory, vaccineType, live, deleted);
        await db.SaveChangesAsync();

        var sixtyDaysAgo = DateTime.UtcNow.Date.AddDays(-60);

        foreach (var animal in new[] { live, deleted })
        {
            db.VaccinationRecords.Add(new VaccinationRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                AnimalId = animal.Id,
                VaccineTypeId = vaccineType.Id,
                DateGiven = sixtyDaysAgo,
                Cost = 10m
            });

            db.WeightRecords.Add(new WeightRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                AnimalId = animal.Id,
                WeightKg = 400m,
                RecordedAt = sixtyDaysAgo
            });
        }

        // One schedule of each kind, with no criteria, so it matches every animal it is offered.
        // RecurrenceDays 30 against a record 60 days old makes every answer "overdue".
        db.VaccinationSchedules.Add(new VaccinationSchedule
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            VaccineTypeId = vaccineType.Id,
            RecurrenceDays = 30,
            IsActive = true
        });
        db.WeightCheckSchedules.Add(new WeightCheckSchedule
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            RecurrenceDays = 30,
            IsActive = true
        });

        await db.SaveChangesAsync();

        var user = new FixedCurrentUser();

        // ── the report: excluded, so one pair, not two ──
        var report = (await new ReportService(db).GetVaccinationReportAsync(farmId, null, null)).Value!;
        Assert.Equal(1, report.OverdueCount + report.UpcomingCount);

        // ── the status path: included, so two entries ──
        var vaccinationStatuses = (await new VaccineService(db, user).GetVaccinationStatusAsync(farmId)).Value!;
        Assert.Equal(2, vaccinationStatuses.Count);
        Assert.Contains(vaccinationStatuses, s => s.AnimalTagNumber == "GONE-1");
        Assert.Contains(vaccinationStatuses, s => s.AnimalTagNumber == "LIVE-1");

        // ── the weight-check path: excluded again ──
        var weightChecks = (await new WeightCheckScheduleService(db, user).GetWeightCheckStatusAsync(farmId)).Value!;
        Assert.Single(weightChecks);
        Assert.Equal("LIVE-1", weightChecks[0].AnimalTagNumber);

        // ── and the feeding path excludes it too, for the same "no longer work anyone can do"
        //    reason the weight-check path does ──
        var feedType = new FeedType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Hay" };
        db.FeedTypes.Add(feedType);

        var plan = new DietPlan { Id = Guid.NewGuid(), FarmId = farmId, Name = "Ration", IsActive = true };
        db.DietPlans.Add(plan);
        db.DietPlanItems.Add(new DietPlanItem
        {
            Id = Guid.NewGuid(),
            DietPlanId = plan.Id,
            FeedTypeId = feedType.Id,
            QuantityPerFeeding = 5m
        });
        db.FeedingSchedules.Add(new FeedingSchedule
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            DietPlanId = plan.Id,
            TimeOfDay = new TimeSpan(6, 0, 0),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var tasks = (await new FeedService(db, user).GenerateTasksAsync(
            farmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date })).Value!;

        Assert.Single(tasks);
        Assert.Equal(1, tasks[0].TargetAnimalCount);
    }
}
