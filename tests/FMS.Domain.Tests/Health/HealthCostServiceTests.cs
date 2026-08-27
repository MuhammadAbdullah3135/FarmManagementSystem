using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class HealthCostServiceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static HealthCostService CreateService(FmsDbContext context) => new(context);

    private sealed record SeedData(
        Guid FarmId,
        Guid AnimalId,
        Guid OtherAnimalId,
        Guid VaccineTypeId,
        Guid VeterinaryCategoryId,
        Guid CashMethodId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var now = DateTime.UtcNow;
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };

        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var activeStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };
        var vetCategory = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Veterinary" };
        var cashMethod = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        var vaccineType = new VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Brucellosis" };

        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-001",
            Name = "Bella",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = activeStatus.Id
        };
        var otherAnimal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-002",
            Name = "Max",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = activeStatus.Id
        };

        // Medical records with a cost, current month
        var med1 = new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            Symptoms = "Fever",
            Diagnosis = "Mastitis",
            VetName = "Dr. Alpha",
            Cost = 100m,
            DateRecorded = now.Date
        };
        var med2 = new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = otherAnimal.Id,
            Symptoms = "Injury",
            Diagnosis = "Cut",
            VetName = "Dr. Alpha",
            Cost = 50m,
            DateRecorded = now.Date
        };
        // Medical record with zero cost (should be ignored)
        var med0 = new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            Symptoms = "Checkup",
            Diagnosis = "Routine",
            VetName = "Dr. Beta",
            Cost = 0m,
            DateRecorded = now.Date
        };
        // Vaccination records, current month + previous month
        var vax1 = new VaccinationRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            VaccineTypeId = vaccineType.Id,
            VetName = "Dr. Alpha",
            Cost = 60m,
            DateGiven = now.Date
        };
        var vax2 = new VaccinationRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            VaccineTypeId = vaccineType.Id,
            VetName = "Dr. Gamma",
            Cost = 40m,
            DateGiven = now.Date.AddMonths(-1)
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(activeStatus);
        context.ExpenseCategories.Add(vetCategory);
        context.PaymentMethods.Add(cashMethod);
        context.VaccineTypes.Add(vaccineType);
        context.Animals.AddRange(animal, otherAnimal);
        context.MedicalRecords.AddRange(med1, med2, med0);
        context.VaccinationRecords.AddRange(vax1, vax2);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, animal.Id, otherAnimal.Id, vaccineType.Id, vetCategory.Id, cashMethod.Id);
    }

    [Fact]
    public async Task Summary_AggregatesMedicalAndVaccinationCosts()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var summary = await service.GetCostSummaryAsync(seed.FarmId, new HealthCostFilter());

        Assert.True(summary.IsSuccess);
        Assert.Equal(150m, summary.Value!.TotalMedicalCost);   // 100 + 50
        Assert.Equal(2, summary.Value.MedicalRecordCount);     // zero-cost record excluded
        Assert.Equal(100m, summary.Value.TotalVaccinationCost); // 60 + 40
        Assert.Equal(2, summary.Value.VaccinationRecordCount);
        Assert.Equal(250m, summary.Value.GrandTotal);
    }

    [Fact]
    public async Task Summary_WithDateFilter_OnlyCountsMatchingRecords()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Only current month (exclude the -1 month vaccination)
        var summary = await service.GetCostSummaryAsync(seed.FarmId, new HealthCostFilter
        {
            From = DateTime.UtcNow.Date.AddDays(-7),
            To = DateTime.UtcNow.Date.AddDays(1)
        });

        Assert.True(summary.IsSuccess);
        Assert.Equal(150m, summary.Value!.TotalMedicalCost);
        Assert.Equal(60m, summary.Value.TotalVaccinationCost); // only current-month vaccination (60)
        Assert.Equal(1, summary.Value.VaccinationRecordCount);
        Assert.Equal(210m, summary.Value.GrandTotal);
    }

    [Fact]
    public async Task CostsByVet_MergesMedicalAndVaccinationByName()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetCostsByVetAsync(seed.FarmId, new HealthCostFilter());

        Assert.True(result.IsSuccess);

        var vals = result.Value!;
        var alpha = vals.Single(v => v.VetName == "Dr. Alpha");
        Assert.Equal(210m, alpha.TotalCost); // 100 + 50 + 60
        Assert.Equal(3, alpha.RecordCount);

        var gamma = vals.Single(v => v.VetName == "Dr. Gamma");
        Assert.Equal(40m, gamma.TotalCost);

        // Beta's only medical record is zero-cost, so excluded
        Assert.DoesNotContain(vals, v => v.VetName == "Dr. Beta");

        // Sorted by total cost descending
        Assert.Equal("Dr. Alpha", vals[0].VetName);
    }

    [Fact]
    public async Task CostsByAnimal_AggregatesAcrossBothRecordTypes()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetCostsByAnimalAsync(seed.FarmId, new HealthCostFilter());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var bella = result.Value.Single(a => a.AnimalId == seed.AnimalId);
        Assert.Equal("TAG-001", bella.TagNumber);
        Assert.Equal(200m, bella.TotalCost);   // 100 (med) + 60 (vax current) + 40 (vax prev)
        Assert.Equal(3, bella.RecordCount);

        var max = result.Value.Single(a => a.AnimalId == seed.OtherAnimalId);
        Assert.Equal(50m, max.TotalCost);      // 50 (med)
        Assert.Equal(1, max.RecordCount);
    }

    [Fact]
    public async Task CostsByMonth_ReturnsTwelveMonths_WithCorrectTotals()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var year = DateTime.UtcNow.Year;
        var result = await service.GetCostsByMonthAsync(seed.FarmId, year);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value!.Count);

        var currentMonth = DateTime.UtcNow.Month - 1; // index
        var thisMonth = result.Value[currentMonth];
        Assert.Equal(150m, thisMonth.MedicalCost);
        Assert.Equal(60m, thisMonth.VaccinationCost);
        Assert.Equal(210m, thisMonth.Total);

        // The -1 month vaccination only falls inside the queried year when the current
        // month isn't January (otherwise it lands in the previous year and is excluded).
        if (DateTime.UtcNow.Month > 1)
        {
            var prevMonthIdx = DateTime.UtcNow.Month - 2; // 0-based index of last month
            Assert.Equal(40m, result.Value[prevMonthIdx].VaccinationCost);
        }

        Assert.All(result.Value, m => Assert.Equal(year, m.Year));
        Assert.Equal("January", result.Value[0].MonthName);
        Assert.Equal("December", result.Value[11].MonthName);
    }

    [Fact]
    public async Task CostsByAnimal_HandlesAnimalWithOnlyZeroCostRecords()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // New animal with only a zero-cost record. Because the group-by key relies on
        // the record having cost > 0, this animal won't appear in the rollup.
        var type = await context.AnimalTypes.FirstAsync();
        var sex = await context.SexOptions.FirstAsync();
        var status = await context.AnimalStatuses.FirstAsync();
        var noCostAnimal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            TagNumber = "TAG-003",
            Name = "N/A",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };
        context.Animals.Add(noCostAnimal);
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = noCostAnimal.Id,
            Symptoms = "Checkup",
            VetName = "Dr. None",
            Cost = 0m,
            DateRecorded = DateTime.UtcNow.Date
        });
        await context.SaveChangesAsync();

        var result = await service.GetCostsByAnimalAsync(seed.FarmId, new HealthCostFilter());
        Assert.DoesNotContain(result.Value!, a => a.AnimalId == noCostAnimal.Id);
        Assert.Equal(2, result.Value!.Count);
    }
}