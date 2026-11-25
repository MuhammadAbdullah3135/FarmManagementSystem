using FMS.Application.Common;
using FMS.Application.Breeding;
using FMS.Application.Finance;
using FMS.Domain.Enums;
using FMS.Infrastructure.Breeding;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// Cross-module service checks: Finance date-preservation on update, Breeding sire linkage on birth.
/// </summary>
public class CrossModuleDateAndLinkTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => null;
    }

    // ── Finance: year-1 date reset on update ─────────────────────────

    private static FinanceService CreateFinanceService(FmsDbContext context) =>
        new(context, new FixedCurrentUser(),
            new FileStorageService(Path.Combine(Path.GetTempPath(), "fms-crossmodule-unit-uploads")));

    [Fact]
    public async Task UpdateExpense_OmittingDate_ResetsToYear1()
    {
        using var context = CreateContext();
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var cat = new Domain.Entities.ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        var method = new Domain.Entities.PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        context.Farms.Add(farm);
        context.ExpenseCategories.Add(cat);
        context.PaymentMethods.Add(method);
        await context.SaveChangesAsync();

        var svc = CreateFinanceService(context);
        var expenseDate = DateTime.UtcNow.Date.AddDays(-10);

        var created = await svc.CreateExpenseAsync(farm.Id, new CreateExpenseRequest
        {
            Amount = 100m,
            ExpenseCategoryId = cat.Id,
            PaymentMethodId = method.Id,
            ExpenseDate = expenseDate,
            Description = "feed"
        });
        Assert.True(created.IsSuccess);
        Assert.Equal(expenseDate, created.Value!.ExpenseDate.Date);

        // Update omitting ExpenseDate -> non-nullable DateTime defaults to 0001-01-01
        var updated = await svc.UpdateExpenseAsync(farm.Id, created.Value.Id, new UpdateExpenseRequest
        {
            Amount = 200m,
            ExpenseCategoryId = cat.Id,
            PaymentMethodId = method.Id
            // ExpenseDate left at DateTime.MinValue
        });
        Assert.True(updated.IsSuccess);

        var entity = await context.Expenses.SingleAsync(e => e.Id == created.Value.Id);

        // EXPECTED: date preserved when not provided.
        // ACTUAL (verified bug): date silently reset to year 1.
        Assert.True(entity.ExpenseDate.Year > 2000,
            $"Update omitting ExpenseDate should preserve the original date. Actual: {entity.ExpenseDate:yyyy-MM-dd}.");
    }

    [Fact]
    public async Task UpdateIncome_OmittingDate_ResetsToYear1()
    {
        using var context = CreateContext();
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var cat = new Domain.Entities.IncomeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Milk" };
        var method = new Domain.Entities.PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        context.Farms.Add(farm);
        context.IncomeCategories.Add(cat);
        context.PaymentMethods.Add(method);
        await context.SaveChangesAsync();

        var svc = CreateFinanceService(context);
        var incomeDate = DateTime.UtcNow.Date.AddDays(-5);

        var created = await svc.CreateIncomeRecordAsync(farm.Id, new CreateIncomeRecordRequest
        {
            Amount = 300m,
            IncomeCategoryId = cat.Id,
            PaymentMethodId = method.Id,
            IncomeDate = incomeDate,
            Description = "milk sale"
        });
        Assert.True(created.IsSuccess);

        var updated = await svc.UpdateIncomeRecordAsync(farm.Id, created.Value!.Id, new UpdateIncomeRecordRequest
        {
            Amount = 400m,
            IncomeCategoryId = cat.Id,
            PaymentMethodId = method.Id
        });
        Assert.True(updated.IsSuccess);

        var entity = await context.IncomeRecords.SingleAsync(e => e.Id == created.Value!.Id);
        Assert.True(entity.IncomeDate.Year > 2000,
            $"Update omitting IncomeDate should preserve the original date. Actual: {entity.IncomeDate:yyyy-MM-dd}.");
    }

    // ── Breeding: offspring sire link on birth ────────────────────────

    private static BreedingService CreateBreedingService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed record BreedSeed(
        Guid FarmId, Guid DamId, Guid SireId, Guid BreedingRecordId,
        Guid MaleSexId, Guid FemaleSexId);

    private static async Task<BreedSeed> SeedBreedingAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Domain.Entities.Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var male = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Male" };
        var female = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var active = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", IsSystemDefined = true };

        var dam = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "DAM-1", Name = "Daisy",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = female.Id, AnimalStatusId = active.Id
        };
        var sire = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "SIRE-1", Name = "Rocky",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = male.Id, AnimalStatusId = active.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.AddRange(male, female);
        context.AnimalStatuses.Add(active);
        context.Animals.AddRange(dam, sire);
        await context.SaveChangesAsync();

        var svc = new BreedingService(context, new FixedCurrentUser());
        var breeding = await svc.CreateBreedingRecordAsync(farm.Id, new CreateBreedingRecordRequest
        {
            SireId = sire.Id, DamId = dam.Id, BreedingDate = DateTime.UtcNow.AddDays(-200),
            Method = BreedingMethod.Natural
        });
        Assert.True(breeding.IsSuccess);

        return new BreedSeed(farm.Id, dam.Id, sire.Id, breeding.Value!.Id, male.Id, female.Id);
    }

    [Fact]
    public async Task Birth_WithBreedingRecord_LinksSire()
    {
        using var context = CreateContext();
        var seed = await SeedBreedingAsync(context);
        var svc = CreateBreedingService(context);

        var birth = await svc.CreateBirthRecordAsync(seed.FarmId, new CreateBirthRecordRequest
        {
            DamId = seed.DamId,
            BreedingRecordId = seed.BreedingRecordId,
            BirthDate = DateTime.UtcNow.AddDays(-1),
            Offspring = new List<CreateBirthOffspringRequest>
            {
                new() { SexOptionId = seed.FemaleSexId, Outcome = BirthOutcome.Alive, BirthWeightKg = 40 }
            }
        });
        Assert.True(birth.IsSuccess);

        var offspring = birth.Value!.Offspring.Single();
        var offspringEntity = await context.Animals.SingleAsync(a => a.Id == offspring.OffspringAnimalId);

        // With a breeding record provided, the sire should be correctly linked.
        Assert.Equal(seed.SireId, offspringEntity.SireId);
    }

    [Fact]
    public async Task GestationList_FilteredByDam_ReturnsOnlyThatDamsRecords()
    {
        using var context = CreateContext();
        var seed = await SeedBreedingAsync(context);
        var svc = CreateBreedingService(context);

        var seededDam = await context.Animals.SingleAsync(a => a.Id == seed.DamId);
        var otherDam = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            TagNumber = "DAM-2",
            Name = "Buttercup",
            AnimalTypeId = seededDam.AnimalTypeId,
            BreedId = seededDam.BreedId,
            SexOptionId = seededDam.SexOptionId,
            AnimalStatusId = seededDam.AnimalStatusId
        };
        context.Animals.Add(otherDam);
        await context.SaveChangesAsync();

        var otherMating = await svc.CreateBreedingRecordAsync(seed.FarmId, new CreateBreedingRecordRequest
        {
            SireId = seed.SireId, DamId = otherDam.Id, BreedingDate = DateTime.UtcNow.AddDays(-120),
            Method = BreedingMethod.Natural
        });
        Assert.True(otherMating.IsSuccess);

        foreach (var matingId in new[] { seed.BreedingRecordId, otherMating.Value!.Id })
        {
            var confirmed = await svc.ConfirmPregnancyAsync(seed.FarmId, new ConfirmPregnancyRequest
            {
                BreedingRecordId = matingId, ConfirmedDate = DateTime.UtcNow.AddDays(-30)
            });
            Assert.True(confirmed.IsSuccess);
        }

        var forSeededDam = await svc.GetGestationRecordsAsync(seed.FarmId, new GestationRecordListFilter { DamId = seed.DamId });
        Assert.True(forSeededDam.IsSuccess);
        Assert.Equal(seed.DamId, Assert.Single(forSeededDam.Value!.Items).AnimalId);

        var forOtherDam = await svc.GetGestationRecordsAsync(seed.FarmId, new GestationRecordListFilter { DamId = otherDam.Id });
        Assert.True(forOtherDam.IsSuccess);
        Assert.Equal(otherDam.Id, Assert.Single(forOtherDam.Value!.Items).AnimalId);

        // Without the filter the farm still lists both, so the filter narrows rather than replaces.
        var unfiltered = await svc.GetGestationRecordsAsync(seed.FarmId, new GestationRecordListFilter());
        Assert.Equal(2, unfiltered.Value!.TotalCount);
    }

    [Fact]
    public async Task Birth_WithoutBreedingOrGestation_IsRejected()
    {
        using var context = CreateContext();
        var seed = await SeedBreedingAsync(context);
        var svc = CreateBreedingService(context);

        // No BreedingRecordId / GestationRecordId -> sire should still be derivable only if a link exists.
        // The breeding record exists for this dam, but birth does not reference it.
        var birth = await svc.CreateBirthRecordAsync(seed.FarmId, new CreateBirthRecordRequest
        {
            DamId = seed.DamId,
            BirthDate = DateTime.UtcNow.AddDays(-1),
            Offspring = new List<CreateBirthOffspringRequest>
            {
                new() { SexOptionId = seed.FemaleSexId, Outcome = BirthOutcome.Alive, BirthWeightKg = 38 }
            }
        });
        Assert.False(birth.IsSuccess);
        Assert.Contains("breeding record or gestation record is required", birth.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
