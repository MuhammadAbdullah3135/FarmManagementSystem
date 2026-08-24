using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FeedRecordingTests
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
        Guid FarmId, Guid FeedTypeId, Guid AnimalId, Guid LocationId);

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
            TagNumber = "F-001",
            Name = "Bessie",
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id,
            LocationId = location.Id
        };
        var feedType = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Alfalfa Hay",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 5m
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.Animals.Add(animal);
        context.FeedTypes.Add(feedType);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, feedType.Id, animal.Id, location.Id);
    }

    private static CreateFeedRecordRequest IndividualRequest(SeedData seed, decimal quantity = 3m) => new()
    {
        FeedTypeId = seed.FeedTypeId,
        AnimalId = seed.AnimalId,
        Quantity = quantity
    };

    [Fact]
    public async Task CreateFeedRecord_Individual_Succeeds_DeductsStock_WritesTimelineEvent()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });

        var result = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        Assert.True(result.IsSuccess);
        Assert.Equal("Alfalfa Hay", result.Value!.FeedTypeName);
        Assert.Equal("Kilogram", result.Value.UnitName);
        Assert.Equal("F-001", result.Value.AnimalTagNumber);
        Assert.Equal(15m, result.Value.TotalCost);

        var stock = await service.GetStockByFeedTypeAsync(seed.FarmId, seed.FeedTypeId);
        Assert.Equal(97m, stock.Value!.CurrentStock);

        var events = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.Fed)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal("Fed 3 kg Alfalfa Hay", events[0].Title);
        Assert.Equal("FeedRecord", events[0].RelatedEntityType);
        Assert.Equal(result.Value.Id, events[0].RelatedEntityId);
    }

    [Fact]
    public async Task CreateFeedRecord_GroupByLocation_Succeeds_DeductsStock_NoTimelineEvent()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });

        var result = await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            LocationId = seed.LocationId,
            Quantity = 20
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Barn 2", result.Value!.LocationName);
        Assert.Null(result.Value.AnimalId);

        var stock = await service.GetStockByFeedTypeAsync(seed.FarmId, seed.FeedTypeId);
        Assert.Equal(80m, stock.Value!.CurrentStock);

        Assert.False(await context.AnimalTimelineEvents.AnyAsync(e => e.EventType == TimelineEventTypes.Fed));
    }

    [Fact]
    public async Task CreateFeedRecord_BothTargets_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            AnimalId = seed.AnimalId,
            LocationId = seed.LocationId,
            Quantity = 3
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateFeedRecord_NoTarget_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            Quantity = 3
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateFeedRecord_InsufficientStock_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 2 });

        var result = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Empty(await context.FeedRecords.ToListAsync());
    }

    [Fact]
    public async Task CreateFeedRecord_UnknownAnimal_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            AnimalId = Guid.NewGuid(),
            Quantity = 3
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task CreateFeedRecord_NonPositiveQuantity_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 0));

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateFeedRecord_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            AnimalId = seed.AnimalId,
            Quantity = 3,
            FedAt = DateTime.UtcNow.AddDays(1)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateFeedRecord_IncreaseQuantity_AdjustsStock_AndWritesEvent()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });
        var created = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        var result = await service.UpdateFeedRecordAsync(seed.FarmId, created.Value!.Id,
            new UpdateFeedRecordRequest { Quantity = 5 });

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.Quantity);

        var stock = await service.GetStockByFeedTypeAsync(seed.FarmId, seed.FeedTypeId);
        Assert.Equal(95m, stock.Value!.CurrentStock);

        var updates = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.FeedUpdated)
            .ToListAsync();
        Assert.Single(updates);
        Assert.Contains("3", updates[0].Title);
        Assert.Contains("5", updates[0].Title);
    }

    [Fact]
    public async Task UpdateFeedRecord_IncreaseBeyondStock_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 4 });
        var created = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        var result = await service.UpdateFeedRecordAsync(seed.FarmId, created.Value!.Id,
            new UpdateFeedRecordRequest { Quantity = 6 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteFeedRecord_RestoresStock_WritesEvent()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });
        var created = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        var deleteResult = await service.DeleteFeedRecordAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleteResult.IsSuccess);

        var stock = await service.GetStockByFeedTypeAsync(seed.FarmId, seed.FeedTypeId);
        Assert.Equal(100m, stock.Value!.CurrentStock);

        Assert.Empty(await context.FeedRecords.ToListAsync());

        var deletions = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.FeedDeleted)
            .ToListAsync();
        Assert.Single(deletions);
    }

    [Fact]
    public async Task GetFeedRecords_FiltersAndPaging_NewestFirst()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });

        for (var day = 3; day >= 1; day--)
        {
            await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
            {
                FeedTypeId = seed.FeedTypeId,
                AnimalId = seed.AnimalId,
                Quantity = day,
                FedAt = DateTime.UtcNow.AddDays(-day)
            });
        }

        await service.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = seed.FeedTypeId,
            LocationId = seed.LocationId,
            Quantity = 10,
            FedAt = DateTime.UtcNow.AddHours(-1)
        });

        var all = await service.GetFeedRecordsAsync(seed.FarmId, new FeedRecordListFilter());
        Assert.Equal(4, all.Value!.TotalCount);
        Assert.Equal(10m, all.Value.Items[0].Quantity);

        var byAnimal = await service.GetFeedRecordsAsync(seed.FarmId, new FeedRecordListFilter { AnimalId = seed.AnimalId });
        Assert.Equal(3, byAnimal.Value!.TotalCount);
        Assert.All(byAnimal.Value.Items, r => Assert.NotNull(r.AnimalId));

        var fromDate = await service.GetFeedRecordsAsync(seed.FarmId, new FeedRecordListFilter
        {
            From = DateTime.UtcNow.AddDays(-1.5)
        });
        Assert.Equal(2, fromDate.Value!.TotalCount);

        var paged = await service.GetFeedRecordsAsync(seed.FarmId, new FeedRecordListFilter { Page = 2, PageSize = 3 });
        Assert.Equal(4, paged.Value!.TotalCount);
        Assert.Single(paged.Value.Items);
    }

    [Fact]
    public async Task GetFeedRecordById_ReturnsNames()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(seed.FarmId,
            new RecordStockMovementRequest { FeedTypeId = seed.FeedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });
        var created = await service.CreateFeedRecordAsync(seed.FarmId, IndividualRequest(seed, 3));

        var result = await service.GetFeedRecordByIdAsync(seed.FarmId, created.Value!.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alfalfa Hay", result.Value!.FeedTypeName);
        Assert.Equal("Bessie", result.Value.AnimalName);
        Assert.Null(result.Value.LocationName);
    }
}
