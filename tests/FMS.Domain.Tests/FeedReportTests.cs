using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FeedReportTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static FeedService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(
        Guid FarmId, Guid AlfalfaId, Guid SilageId, Guid AnimalId, Guid LocationId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn 2", LocationTypeId = locationType.Id };
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "R-001",
            Name = "Bessie",
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id,
            LocationId = location.Id
        };
        var alfalfa = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Alfalfa Hay",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 5m
        };
        var silage = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Corn Silage",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 2m
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.Animals.Add(animal);
        context.FeedTypes.AddRange(alfalfa, silage);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, alfalfa.Id, silage.Id, animal.Id, location.Id);
    }

    /// <summary>Purchases ample stock dated 60 days ago so back-dated feed records pass stock checks.</summary>
    private static async Task StockUpAsync(FeedService service, SeedData seed)
    {
        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest
            {
                FeedTypeId = seed.AlfalfaId,
                MovementType = StockMovementType.Purchase,
                Quantity = 1000,
                UnitCost = 5m,
                MovementDate = DateTime.UtcNow.AddDays(-60)
            });
        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest
            {
                FeedTypeId = seed.SilageId,
                MovementType = StockMovementType.Purchase,
                Quantity = 1000,
                UnitCost = 2m,
                MovementDate = DateTime.UtcNow.AddDays(-60)
            });
    }

    [Fact]
    public async Task GetConsumptionTrend_DailyBuckets_AggregatesQuantityAndCost()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        var yesterday = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var today = new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 3,
            FedAt = yesterday.AddHours(7)
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 4,
            FedAt = yesterday.AddHours(16)
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.SilageId, LocationId = seed.LocationId, Quantity = 10,
            FedAt = today.AddHours(7)
        });

        var result = await service.GetConsumptionTrendAsync(seed.FarmId, "day",
            yesterday.AddDays(-1), today.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var yesterdayBucket = result.Value[0];
        Assert.Equal(yesterday, yesterdayBucket.PeriodStart);
        Assert.Equal(7m, yesterdayBucket.Quantity);
        Assert.Equal(35m, yesterdayBucket.Cost);

        var todayBucket = result.Value[1];
        Assert.Equal(today, todayBucket.PeriodStart);
        Assert.Equal(10m, todayBucket.Quantity);
        Assert.Equal(20m, todayBucket.Cost);
    }

    [Fact]
    public async Task GetConsumptionTrend_MonthlyBuckets_GroupsAcrossMonths()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 100,
            FedAt = DateTime.UtcNow.AddDays(-40)
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 50,
            FedAt = DateTime.UtcNow.AddDays(-5)
        });

        var result = await service.GetConsumptionTrendAsync(seed.FarmId, "month",
            DateTime.UtcNow.AddDays(-60), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value, p => Assert.Matches(@"^\d{4}-\d{2}$", p.Label));
        Assert.True(result.Value[0].Quantity < result.Value[1].Quantity || result.Value[0].PeriodStart < result.Value[1].PeriodStart);
    }

    [Fact]
    public async Task GetConsumptionTrend_InvalidPeriodOrRange_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var badPeriod = await service.GetConsumptionTrendAsync(seed.FarmId, "hourly", null, null);
        Assert.False(badPeriod.IsSuccess);
        Assert.Equal("Validation", badPeriod.Error!.Code);

        var badRange = await service.GetConsumptionTrendAsync(seed.FarmId, "day",
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(-5));
        Assert.False(badRange.IsSuccess);
        Assert.Equal("Validation", badRange.Error!.Code);
    }

    [Fact]
    public async Task GetConsumptionByFeedType_ReturnsSharesOrderedByCost()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 10 // 50 cost
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.SilageId, LocationId = seed.LocationId, Quantity = 20 // 40 cost
        });

        var result = await service.GetConsumptionByFeedTypeAsync(seed.FarmId, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var top = result.Value[0];
        Assert.Equal("Alfalfa Hay", top.FeedTypeName);
        Assert.Equal(50m, top.Cost);
        Assert.Equal(55.56m, top.SharePercent);

        Assert.Equal(44.44m, result.Value[1].SharePercent);
        Assert.Equal(100m, result.Value.Sum(x => x.SharePercent));
    }

    [Fact]
    public async Task GetConsumptionByAnimal_OnlyIndividualFeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 6
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, LocationId = seed.LocationId, Quantity = 25
        });

        var result = await service.GetConsumptionByAnimalAsync(seed.FarmId, null, null);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Equal("R-001", row.TagNumber);
        Assert.Equal("Bessie", row.Name);
        Assert.Equal(6m, row.Quantity);
        Assert.Equal(30m, row.Cost);
    }

    [Fact]
    public async Task GetConsumptionByLocation_OnlyGroupFeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.SilageId, LocationId = seed.LocationId, Quantity = 15
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.SilageId, AnimalId = seed.AnimalId, Quantity = 4
        });

        var result = await service.GetConsumptionByLocationAsync(seed.FarmId, null, null);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Equal("Barn 2", row.LocationName);
        Assert.Equal(15m, row.Quantity);
        Assert.Equal(30m, row.Cost);
    }

    [Fact]
    public async Task GetCostSummary_ComputesPurchasedConsumedAndInventoryValue()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed); // 1000 @ 5 = 5000; 1000 @ 2 = 2000

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 10 // 50
        });
        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.SilageId, LocationId = seed.LocationId, Quantity = 20 // 40
        });

        var result = await service.GetCostSummaryAsync(seed.FarmId,
            DateTime.UtcNow.Date.AddDays(-60), DateTime.UtcNow.Date);

        Assert.True(result.IsSuccess);
        Assert.Equal(30m, result.Value!.TotalConsumedQuantity);
        Assert.Equal(90m, result.Value.TotalConsumedCost);
        Assert.Equal(2000m, result.Value.TotalPurchasedQuantity);
        Assert.Equal(7000m, result.Value.TotalPurchasedCost);
        Assert.Equal((1000 - 10) * 5m + (1000 - 20) * 2m, result.Value.CurrentInventoryValue);
    }

    [Fact]
    public async Task Reports_AreFarmScoped()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);
        await StockUpAsync(service, seed);

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.AlfalfaId, AnimalId = seed.AnimalId, Quantity = 5
        });

        var otherFarm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Other" };
        context.Farms.Add(otherFarm);
        await context.SaveChangesAsync();

        var trend = await service.GetConsumptionTrendAsync(otherFarm.Id, "day", null, null);
        Assert.Empty(trend.Value!);

        var byType = await service.GetConsumptionByFeedTypeAsync(otherFarm.Id, null, null);
        Assert.Empty(byType.Value!);

        var summary = await service.GetCostSummaryAsync(otherFarm.Id, null, null);
        Assert.Equal(0m, summary.Value!.TotalConsumedCost);
        Assert.Equal(0m, summary.Value.CurrentInventoryValue);
    }
}
