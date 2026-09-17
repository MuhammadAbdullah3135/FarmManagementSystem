using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FarmDefaultsSeederTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static async Task<Farm> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Defaults Test Farm" };
        context.Farms.Add(farm);
        await context.SaveChangesAsync();
        return farm;
    }

    private sealed record Counts(int Statuses, int SexOptions, int AgeCategories, int LocationTypes, int Locations, int AnimalTypes, int Breeds);

    private static async Task<Counts> CountAsync(FmsDbContext db, Guid farmId)
    {
        var breeds = await db.Breeds.Where(b => b.AnimalType.FarmId == farmId).CountAsync();
        return new Counts(
            await db.AnimalStatuses.CountAsync(s => s.FarmId == farmId),
            await db.SexOptions.CountAsync(s => s.FarmId == farmId),
            await db.AgeCategories.CountAsync(c => c.FarmId == farmId),
            await db.LocationTypes.CountAsync(lt => lt.FarmId == farmId),
            await db.Locations.CountAsync(l => l.FarmId == farmId),
            await db.AnimalTypes.CountAsync(t => t.FarmId == farmId),
            breeds);
    }

    private static readonly Counts FullDefaults = new(Statuses: 6, SexOptions: 2, AgeCategories: 3, LocationTypes: 5, Locations: 1, AnimalTypes: 5, Breeds: 7);

    private static void AssertFullDefaults(Counts actual)
    {
        Assert.Equal(FullDefaults.Statuses, actual.Statuses);
        Assert.Equal(FullDefaults.SexOptions, actual.SexOptions);
        Assert.Equal(FullDefaults.AgeCategories, actual.AgeCategories);
        Assert.Equal(FullDefaults.LocationTypes, actual.LocationTypes);
        Assert.Equal(FullDefaults.Locations, actual.Locations);
        Assert.Equal(FullDefaults.AnimalTypes, actual.AnimalTypes);
        Assert.Equal(FullDefaults.Breeds, actual.Breeds);
    }

    [Fact]
    public async Task FreshFarm_IsSeededWithAllDefaultLookupCategories()
    {
        await using var db = CreateContext();
        var farm = await SeedFarmAsync(db);

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);

        AssertFullDefaults(await CountAsync(db, farm.Id));

        // Sex options / statuses seeded with the values the forms expect.
        var sexValues = await db.SexOptions.Where(s => s.FarmId == farm.Id).Select(s => s.Value).ToListAsync();
        Assert.Contains("Male", sexValues);
        Assert.Contains("Female", sexValues);

        // Animal types must carry their default breeds.
        var cattle = await db.AnimalTypes.Include(t => t.Breeds).SingleAsync(t => t.FarmId == farm.Id && t.Name == "Cattle");
        Assert.Contains(cattle.Breeds, b => b.Name == "Sahiwal");
        Assert.Contains(cattle.Breeds, b => b.Name == "Holstein Friesian");
    }

    [Fact]
    public async Task SecondCall_DoesNotDuplicateDefaults()
    {
        await using var db = CreateContext();
        var farm = await SeedFarmAsync(db);

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);
        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);

        AssertFullDefaults(await CountAsync(db, farm.Id));
    }

    [Fact]
    public async Task PartialFarm_GetsOnlyMissingCategoriesBackfilled()
    {
        await using var db = CreateContext();
        var farm = await SeedFarmAsync(db);

        // Registration-era farms only received animal statuses.
        db.AnimalStatuses.Add(new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active, IsSystemDefined = true });
        await db.SaveChangesAsync();

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);

        var counts = await CountAsync(db, farm.Id);
        Assert.Equal(1, counts.Statuses); // existing statuses untouched
        Assert.Equal(FullDefaults.SexOptions, counts.SexOptions);
        Assert.Equal(FullDefaults.AgeCategories, counts.AgeCategories);
        Assert.Equal(FullDefaults.LocationTypes, counts.LocationTypes);
        Assert.Equal(FullDefaults.Locations, counts.Locations);
        Assert.Equal(FullDefaults.AnimalTypes, counts.AnimalTypes);
        Assert.Equal(FullDefaults.Breeds, counts.Breeds);
    }

    [Fact]
    public async Task FarmWithCustomLookups_IsNotOverwritten()
    {
        await using var db = CreateContext();
        var farm = await SeedFarmAsync(db);

        db.SexOptions.Add(new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Unknown" });
        db.AgeCategories.Add(new AgeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Juvenile", MinDays = 1, MaxDays = 2 });
        var camelType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Camel" };
        db.AnimalTypes.Add(camelType);
        db.Breeds.Add(new Breed { Id = Guid.NewGuid(), AnimalTypeId = camelType.Id, Name = "Dromedary", AverageGestationDays = 390 });
        var barnType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn" };
        db.LocationTypes.Add(barnType);
        db.Locations.Add(new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Camel Pen", LocationTypeId = barnType.Id });
        await db.SaveChangesAsync();

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);

        var counts = await CountAsync(db, farm.Id);
        Assert.Equal(1, counts.SexOptions);   // user's own sex options kept
        Assert.Equal(1, counts.AgeCategories);
        Assert.Equal(6, counts.AnimalTypes);  // Camel kept + 5 defaults added
        Assert.Equal(8, counts.Breeds);       // Dromedary kept + 7 default breeds added
        Assert.Equal(1, counts.LocationTypes);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(FullDefaults.Statuses, counts.Statuses); // statuses were absent and are seeded
    }

    [Fact]
    public async Task FarmWithInlineCreatedType_GetsBreedsBackfilledWhenItHasNone()
    {
        await using var db = CreateContext();
        var farm = await SeedFarmAsync(db);

        // Simulates the Add Animal quick-add path: a type exists but no breeds at all.
        var cattle = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        db.AnimalTypes.Add(cattle);
        await db.SaveChangesAsync();

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(db, farm.Id);

        var breeds = await db.Breeds.Where(b => b.AnimalTypeId == cattle.Id).Select(b => b.Name).ToListAsync();
        Assert.Contains("Sahiwal", breeds);
        Assert.Contains("Holstein Friesian", breeds);
    }

    [Fact]
    public async Task FarmService_CreateFarm_SeedsAllDefaultLookupCategories()
    {
        await using var db = CreateContext();
        var service = new FMS.Infrastructure.Farm.FarmService(db);

        var result = await service.CreateFarmAsync(Guid.NewGuid(), Guid.NewGuid(), new FMS.Application.Farm.CreateFarmRequest
        {
            Name = "Seeded Via Service",
            Description = null
        });

        Assert.True(result.IsSuccess);
        var createdFarmId = result.Value!.Id;
        AssertFullDefaults(await CountAsync(db, createdFarmId));
    }
}
