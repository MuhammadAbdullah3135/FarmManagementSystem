using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// A farm shaped to expose the per-schedule, per-animal loops.
///
/// <para>
/// Every schedule matches every animal, so the number of (schedule, animal) pairs — the unit the
/// old shape asked the database about one at a time — is its worst case: schedules × animals.
/// That is the number these tests make the code account for.
/// </para>
///
/// <para>
/// Dates are relative to today by design: the due/overdue arithmetic is defined against "today",
/// so a fixture with fixed dates would test a different thing every time it runs. The golden
/// comparison below normalises them back out for exactly that reason.
/// </para>
/// </summary>
internal sealed class ReportPerfFixture
{
    /// <summary>The prefix the weight-check path builds its task titles from.</summary>
    public const string WeightCheckTaskTitlePrefix = "Weight check due: ";

    /// <summary>The date every seeded offset is measured from.</summary>
    public required DateTime Anchor { get; init; }

    public required Guid FarmId { get; init; }
    public required Guid AnimalTypeId { get; init; }
    public required Guid AgeCategoryId { get; init; }
    public required Guid VaccineTypeId { get; init; }
    public required Guid DietPlanId { get; init; }
    public required Guid FeedTypeId { get; init; }
    public required Guid FeedingScheduleId { get; init; }
    public required IReadOnlyList<Guid> AnimalIds { get; init; }

    public required int AnimalCount { get; init; }
    public required int VaccinationSchedules { get; init; }
    public required int WeightCheckSchedules { get; init; }

    /// <summary>Every id this fixture seeded, with the stable name a golden comparison speaks in.</summary>
    public required Dictionary<Guid, string> Labels { get; init; }

    /// <summary>
    /// A stable name for a seeded id. Row ids are freshly generated every run, so a golden
    /// comparison has to use these instead — and an id the fixture does not know about throws
    /// rather than sliding into the output as an always-different value.
    /// </summary>
    public string Label(Guid id) =>
        Labels.TryGetValue(id, out var label)
            ? label
            : throw new InvalidOperationException(
                $"The golden comparison met an id the fixture did not seed ({id}). Map it to a label.");

    /// <summary>
    /// Days from the anchor, which is what makes a golden comparison independent of the day it
    /// runs on. Every seeded date is midnight-anchored, so the offset is exact.
    /// </summary>
    public int Offset(DateTime value) => (value.Date - Anchor.Date).Days;

    public int? Offset(DateTime? value) => value.HasValue ? Offset(value.Value) : null;

    /// <summary>Animals × vaccination schedules: what the old shape queried pair by pair.</summary>
    public int VaccinationPairs => AnimalCount * VaccinationSchedules;

    /// <summary>Animals × weight-check schedules: the same, plus a task lookup per pair.</summary>
    public int WeightCheckPairs => AnimalCount * WeightCheckSchedules;

    /// <param name="anchor">
    /// Where every seeded offset is measured from. Defaults to today, which is what the due and
    /// overdue arithmetic is defined against; pass a fixed date for a golden comparison, where a
    /// shifting anchor would change the answer every time the test ran.
    /// </param>
    /// <param name="animalsWithoutVaccinationRecord">
    /// How many animals to leave unvaccinated, so "never vaccinated" is represented in the
    /// output rather than only in the code.
    /// </param>
    public static async Task<ReportPerfFixture> SeedAsync(
        FmsDbContext db,
        int animalCount,
        int vaccinationSchedules = 2,
        int weightCheckSchedules = 2,
        DateTime? anchor = null,
        int animalsWithoutVaccinationRecord = 0)
    {
        var anchorDate = (anchor ?? DateTime.UtcNow.Date).Date;
        var labels = new Dictionary<Guid, string>();
        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        db.Accounts.Add(new Account { Id = accountId, Name = "Report perf account" });
        db.Farms.Add(new Farm { Id = farmId, AccountId = accountId, Name = "Report perf farm" });

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
        var vaccineType = new VaccineType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "FMD Vaccine",
            DefaultDosage = "5ml"
        };
        var feedType = new FeedType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Hay" };

        db.AddRange(animalType, breed, sex, status, ageCategory, vaccineType, feedType);
        await db.SaveChangesAsync();

        var animals = new List<Animal>(animalCount);
        var animalIds = new List<Guid>(animalCount);

        for (var index = 0; index < animalCount; index++)
        {
            var id = Guid.NewGuid();
            animalIds.Add(id);
            animals.Add(new Animal
            {
                Id = id,
                FarmId = farmId,
                TagNumber = $"RP-{index:D4}",
                Name = $"Cow {index}",
                AnimalTypeId = animalType.Id,
                BreedId = breed.Id,
                SexOptionId = sex.Id,
                AnimalStatusId = status.Id,
                AgeCategoryId = ageCategory.Id,
                DateOfBirth = DateTime.UtcNow.AddYears(-3).AddDays(index % 365)
            });
        }

        // Batched: a single 400-row insert per batch keeps the fixture cheap on SQLite.
        foreach (var batch in animals.Chunk(500))
            db.Animals.AddRange(batch);
        await db.SaveChangesAsync();

        // Vaccination history. The offsets spread the animals across overdue, due and upcoming,
        // so the report's three branches are all exercised rather than all-overdue.
        // One vaccine type per seeded type would need new ids; a second type is added so the
        // "by vaccine" breakdown has more than one row to order.
        var secondVaccineType = new VaccineType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Brucellosis Vaccine",
            DefaultDosage = "2ml"
        };
        db.VaccineTypes.Add(secondVaccineType);
        await db.SaveChangesAsync();

        var vaccinatedCount = animalCount - Math.Clamp(animalsWithoutVaccinationRecord, 0, animalCount);

        for (var index = 0; index < vaccinatedCount; index++)
        {
            db.VaccinationRecords.Add(new VaccinationRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                AnimalId = animalIds[index],
                // Every third animal is on the other vaccine type, so the breakdown is not flat.
                VaccineTypeId = index % 3 == 0 ? secondVaccineType.Id : vaccineType.Id,
                // Spread so the monthly trend has more than one bucket to order, and so each
                // animal lands on its own due date rather than sharing one.
                DateGiven = anchorDate.AddDays(-(index * 11)),
                Cost = 15m + (index % 5)
            });
        }

        // Weight history, same idea: RecurrenceDays below is 30, so a stale weight is overdue.
        for (var index = 0; index < animalCount; index++)
        {
            db.WeightRecords.Add(new WeightRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                AnimalId = animalIds[index],
                WeightKg = 300m + (index % 200),
                // Stale enough to reach the due and overdue branches, which is where this path
                // also writes: an animal that is due gets a task, so the golden covers the
                // materialised rows and not just the read.
                RecordedAt = anchorDate.AddDays(-(index * 12))
            });
        }

        for (var index = 0; index < vaccinationSchedules; index++)
        {
            var scheduleId = Guid.NewGuid();
            db.VaccinationSchedules.Add(new VaccinationSchedule
            {
                Id = scheduleId,
                FarmId = farmId,
                VaccineTypeId = index % 2 == 0 ? vaccineType.Id : secondVaccineType.Id,
                RecurrenceDays = 30 + index,
                IsActive = true
            });
            labels[scheduleId] = $"vaccination-schedule-{index}";
        }

        for (var index = 0; index < weightCheckSchedules; index++)
        {
            var scheduleId = Guid.NewGuid();
            db.WeightCheckSchedules.Add(new WeightCheckSchedule
            {
                Id = scheduleId,
                FarmId = farmId,
                RecurrenceDays = 30 + index,
                IsActive = true
            });
            labels[scheduleId] = $"weight-check-schedule-{index}";
        }

        await db.SaveChangesAsync();

        // One diet plan with a weight range, so the feed-task path takes its most expensive
        // branch: candidates are filtered by their latest weight, not just by their lookup ids.
        var dietPlan = new DietPlan
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Adult ration",
            MinWeightKg = 300m,
            MaxWeightKg = 600m,
            IsActive = true
        };
        db.DietPlans.Add(dietPlan);
        db.DietPlanItems.Add(new DietPlanItem
        {
            Id = Guid.NewGuid(),
            DietPlanId = dietPlan.Id,
            FeedTypeId = feedType.Id,
            QuantityPerFeeding = 12m
        });
        var feedingScheduleId = Guid.NewGuid();
        db.FeedingSchedules.Add(new FeedingSchedule
        {
            Id = feedingScheduleId,
            FarmId = farmId,
            DietPlanId = dietPlan.Id,
            TimeOfDay = new TimeSpan(7, 30, 0),
            IsActive = true
        });
        await db.SaveChangesAsync();

        labels[farmId] = "farm";
        labels[animalType.Id] = "animal-type";
        labels[ageCategory.Id] = "age-category";
        labels[vaccineType.Id] = "vaccine-type-0";
        labels[secondVaccineType.Id] = "vaccine-type-1";
        labels[feedType.Id] = "feed-type";
        labels[dietPlan.Id] = "diet-plan";
        labels[feedingScheduleId] = "feeding-schedule-0";
        for (var index = 0; index < animalIds.Count; index++)
            labels[animalIds[index]] = $"animal-{index:D2}";

        return new ReportPerfFixture
        {
            Anchor = anchorDate,
            FarmId = farmId,
            AnimalTypeId = animalType.Id,
            AgeCategoryId = ageCategory.Id,
            VaccineTypeId = vaccineType.Id,
            DietPlanId = dietPlan.Id,
            FeedTypeId = feedType.Id,
            FeedingScheduleId = feedingScheduleId,
            AnimalIds = animalIds,
            AnimalCount = animalCount,
            VaccinationSchedules = vaccinationSchedules,
            WeightCheckSchedules = weightCheckSchedules,
            Labels = labels
        };
    }
}
