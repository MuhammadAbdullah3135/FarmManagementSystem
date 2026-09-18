using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Configuration;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// Every foreign key pointing at a configuration lookup is Restrict, so deleting an in-use
/// lookup (animal type, breed, sex option, age category, animal status, location type, location)
/// used to fail inside the database and reach the client as an opaque 500 while the row stayed
/// put. These tests pin the guard that replaced it: a Conflict naming what is in the way, with
/// the row left intact — and a real delete when nothing references it.
/// </summary>
public class ConfigurationDeleteGuardTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static ConfigurationService CreateService(FmsDbContext context) => new(context);

    private sealed record Seed(
        Guid FarmId,
        Guid AnimalTypeId,
        Guid BreedId,
        Guid SexOptionId,
        Guid AgeCategoryId,
        Guid AnimalStatusId,
        Guid LocationTypeId,
        Guid LocationId);

    private static async Task<Seed> SeedLookupsAsync(FmsDbContext context, bool systemDefinedStatus = false)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            AnimalTypeId = animalType.Id,
            AnimalType = animalType,
            Name = "Sahiwal"
        };
        var sexOption = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var ageCategory = new AgeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Adult",
            MinDays = 365,
            MaxDays = 3650
        };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Active",
            Category = AnimalStatusCategory.Active,
            IsSystemDefined = systemDefinedStatus
        };
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Shed" };
        var location = new Location
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Shed A",
            LocationTypeId = locationType.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sexOption);
        context.AgeCategories.Add(ageCategory);
        context.AnimalStatuses.Add(status);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, animalType.Id, breed.Id, sexOption.Id, ageCategory.Id,
            status.Id, locationType.Id, location.Id);
    }

    /// <summary>An animal that references every lookup — the shape that used to break deletes.</summary>
    private static Animal NewAnimal(Seed seed, Guid? animalTypeId = null, Guid? breedId = null) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = seed.FarmId,
        TagNumber = $"TAG-{Guid.NewGuid().ToString()[..8]}",
        AnimalTypeId = animalTypeId ?? seed.AnimalTypeId,
        BreedId = breedId ?? seed.BreedId,
        SexOptionId = seed.SexOptionId,
        AgeCategoryId = seed.AgeCategoryId,
        AnimalStatusId = seed.AnimalStatusId,
        LocationId = seed.LocationId
    };

    // ── Animal type ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnimalType_InUseByAnimal_IsRefused_AndRowSurvives()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Animals.Add(NewAnimal(seed));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteAnimalTypeAsync(seed.FarmId, seed.AnimalTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete animal type 'Cattle'", result.Error.Message);
        Assert.Contains("1 animal", result.Error.Message);
        Assert.Contains("Reassign or delete those records first.", result.Error.Message);
        Assert.True(await context.AnimalTypes.AnyAsync(at => at.Id == seed.AnimalTypeId));
    }

    [Fact]
    public async Task AnimalType_InUseOnlyThroughOneOfItsBreeds_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        // The animal is filed under a different type but carries one of this type's breeds: that
        // breed row cascades away with the type, so the FK would still be violated.
        var otherType = new AnimalType { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Buffalo" };
        context.AnimalTypes.Add(otherType);
        context.Animals.Add(NewAnimal(seed, animalTypeId: otherType.Id));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteAnimalTypeAsync(seed.FarmId, seed.AnimalTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("1 animal", result.Error.Message);
        Assert.True(await context.AnimalTypes.AnyAsync(at => at.Id == seed.AnimalTypeId));
    }

    [Fact]
    public async Task AnimalType_ReferencedOnlyByItsBreeds_DeletesAndCascadesThem()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteAnimalTypeAsync(seed.FarmId, seed.AnimalTypeId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.AnimalTypes.AnyAsync(at => at.Id == seed.AnimalTypeId));
        Assert.False(await context.Breeds.AnyAsync(b => b.Id == seed.BreedId));
    }

    [Fact]
    public async Task AnimalType_OfAnotherFarm_IsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteAnimalTypeAsync(Guid.NewGuid(), seed.AnimalTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    // ── Breed ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Breed_InUseByAnimal_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Animals.Add(NewAnimal(seed));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteBreedAsync(seed.FarmId, seed.BreedId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete breed 'Sahiwal'", result.Error.Message);
        Assert.True(await context.Breeds.AnyAsync(b => b.Id == seed.BreedId));
    }

    [Fact]
    public async Task Breed_InUseByWeightCheckSchedule_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.WeightCheckSchedules.Add(new WeightCheckSchedule
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalTypeId = seed.AnimalTypeId,
            BreedId = seed.BreedId,
            AgeCategoryId = seed.AgeCategoryId,
            RecurrenceDays = 30
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteBreedAsync(seed.FarmId, seed.BreedId);

        Assert.False(result.IsSuccess);
        Assert.Contains("1 weight check schedule", result.Error!.Message);
    }

    [Fact]
    public async Task Breed_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteBreedAsync(seed.FarmId, seed.BreedId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.Breeds.AnyAsync(b => b.Id == seed.BreedId));
    }

    // ── Sex option ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SexOption_InUseByAnimal_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Animals.Add(NewAnimal(seed));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteSexOptionAsync(seed.FarmId, seed.SexOptionId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete sex option 'Female'", result.Error.Message);
        Assert.Contains("1 animal", result.Error.Message);
    }

    [Fact]
    public async Task SexOption_InUseByBirthRecord_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.BirthOffspring.Add(new BirthOffspring
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            BirthRecordId = Guid.NewGuid(),
            TagNumber = "TAG-9001",
            SexOptionId = seed.SexOptionId,
            Outcome = BirthOutcome.Alive
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteSexOptionAsync(seed.FarmId, seed.SexOptionId);

        Assert.False(result.IsSuccess);
        Assert.Contains("1 birth record", result.Error!.Message);
    }

    [Fact]
    public async Task SexOption_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteSexOptionAsync(seed.FarmId, seed.SexOptionId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.SexOptions.AnyAsync(so => so.Id == seed.SexOptionId));
    }

    // ── Age category ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AgeCategory_InUseByAnimal_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Animals.Add(NewAnimal(seed));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteAgeCategoryAsync(seed.FarmId, seed.AgeCategoryId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete age category 'Adult'", result.Error.Message);
        Assert.Contains("1 animal", result.Error.Message);
    }

    [Fact]
    public async Task AgeCategory_InUseByDietPlan_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.DietPlans.Add(new DietPlan
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            Name = "Adult ration",
            AgeCategoryId = seed.AgeCategoryId
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteAgeCategoryAsync(seed.FarmId, seed.AgeCategoryId);

        Assert.False(result.IsSuccess);
        Assert.Contains("1 diet plan", result.Error!.Message);
    }

    [Fact]
    public async Task AgeCategory_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteAgeCategoryAsync(seed.FarmId, seed.AgeCategoryId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.AgeCategories.AnyAsync(ac => ac.Id == seed.AgeCategoryId));
    }

    // ── Animal status ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnimalStatus_SystemDefined_IsRefused_EvenWhenUnused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context, systemDefinedStatus: true);

        var result = await CreateService(context).DeleteAnimalStatusAsync(seed.FarmId, seed.AnimalStatusId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("system status", result.Error.Message);
        Assert.Contains("Deactivate it instead", result.Error.Message);
        Assert.True(await context.AnimalStatuses.AnyAsync(s => s.Id == seed.AnimalStatusId));
    }

    [Fact]
    public async Task AnimalStatus_InUseByAnimal_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Animals.Add(NewAnimal(seed));
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteAnimalStatusAsync(seed.FarmId, seed.AnimalStatusId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete animal status 'Active'", result.Error.Message);
        Assert.Contains("1 animal", result.Error.Message);
    }

    [Fact]
    public async Task AnimalStatus_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteAnimalStatusAsync(seed.FarmId, seed.AnimalStatusId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.AnimalStatuses.AnyAsync(s => s.Id == seed.AnimalStatusId));
    }

    // ── Location type ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LocationType_InUseByLocation_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteLocationTypeAsync(seed.FarmId, seed.LocationTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete location type 'Shed'", result.Error.Message);
        Assert.Contains("1 location", result.Error.Message);
        Assert.True(await context.LocationTypes.AnyAsync(lt => lt.Id == seed.LocationTypeId));
    }

    [Fact]
    public async Task LocationType_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        var spare = new LocationType { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Pasture" };
        context.LocationTypes.Add(spare);
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteLocationTypeAsync(seed.FarmId, spare.Id);

        Assert.True(result.IsSuccess);
        Assert.False(await context.LocationTypes.AnyAsync(lt => lt.Id == spare.Id));
    }

    // ── Location ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Location_WithChildren_IsRefused()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);
        context.Locations.Add(new Location
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            Name = "Shed A1",
            LocationTypeId = seed.LocationTypeId,
            ParentLocationId = seed.LocationId
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteLocationAsync(seed.FarmId, seed.LocationId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Equal("Cannot delete location with children", result.Error.Message);
    }

    [Fact]
    public async Task Location_InUseByAnimalTransfersAndRecords_IsRefused_ListingEveryReference()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var animal = NewAnimal(seed);
        context.Animals.Add(animal);

        var feedType = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            Name = "Alfalfa Hay",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 1m
        };
        var expenseCategory = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Vet" };
        var paymentMethod = new PaymentMethod { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Cash" };
        var incomeCategory = new IncomeCategory { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Milk" };
        context.FeedTypes.Add(feedType);
        context.ExpenseCategories.Add(expenseCategory);
        context.PaymentMethods.Add(paymentMethod);
        context.IncomeCategories.Add(incomeCategory);

        context.FarmTasks.Add(new FarmTask
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            Title = "Clean Shed A",
            DueDate = DateTime.UtcNow.AddDays(1),
            LocationId = seed.LocationId
        });
        context.FeedRecords.Add(new FeedRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            FeedTypeId = feedType.Id,
            LocationId = seed.LocationId,
            Quantity = 5m,
            UnitCost = 1m,
            FedAt = DateTime.UtcNow
        });
        context.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            ExpenseDate = DateTime.UtcNow,
            Amount = 100m,
            ExpenseCategoryId = expenseCategory.Id,
            PaymentMethodId = paymentMethod.Id,
            LocationId = seed.LocationId
        });
        context.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            IncomeDate = DateTime.UtcNow,
            Amount = 200m,
            IncomeCategoryId = incomeCategory.Id,
            PaymentMethodId = paymentMethod.Id,
            LocationId = seed.LocationId
        });
        context.AnimalTransfers.Add(new AnimalTransfer
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = animal.Id,
            ToLocationId = seed.LocationId,
            TransferredAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context).DeleteLocationAsync(seed.FarmId, seed.LocationId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("Cannot delete location 'Shed A'", result.Error.Message);
        Assert.Contains("1 animal", result.Error.Message);
        Assert.Contains("1 transfer", result.Error.Message);
        Assert.Contains("1 expense", result.Error.Message);
        Assert.Contains("1 task", result.Error.Message);
        Assert.Contains("1 feed record", result.Error.Message);
        Assert.Contains("1 income record", result.Error.Message);
        Assert.True(await context.Locations.AnyAsync(l => l.Id == seed.LocationId));
    }

    [Fact]
    public async Task Location_Unused_Deletes()
    {
        using var context = CreateContext();
        var seed = await SeedLookupsAsync(context);

        var result = await CreateService(context).DeleteLocationAsync(seed.FarmId, seed.LocationId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.Locations.AnyAsync(l => l.Id == seed.LocationId));
    }
}
