using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FeedServiceTests
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

    private static async Task<(Guid farmId, Guid feedTypeId)> SeedFeedTypeAsync(
        FmsDbContext context, string name = "Alfalfa Hay",
        FeedUnit unit = FeedUnit.Kilogram, decimal costPerUnit = 5m)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var feedType = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = name,
            Category = FeedCategory.Forage,
            Unit = unit,
            CostPerUnit = costPerUnit
        };

        context.Farms.Add(farm);
        context.FeedTypes.Add(feedType);
        await context.SaveChangesAsync();

        return (farm.Id, feedType.Id);
    }

    [Fact]
    public async Task CreateFeedType_Succeeds()
    {
        using var context = CreateContext();
        var (farmId, _) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedTypeAsync(farmId,
            new CreateFeedTypeRequest { Name = "Corn Silage", Category = FeedCategory.Forage, Unit = FeedUnit.Kilogram, CostPerUnit = 2.5m });

        Assert.True(result.IsSuccess);
        Assert.Equal("Corn Silage", result.Value!.Name);
        Assert.Equal("Kilogram", result.Value.UnitName);
        Assert.Equal(2.5m, result.Value.CostPerUnit);
    }

    [Fact]
    public async Task CreateFeedType_DuplicateName_ReturnsConflict()
    {
        using var context = CreateContext();
        var (farmId, _) = await SeedFeedTypeAsync(context, name: "Alfalfa Hay");
        var service = CreateService(context);

        var result = await service.CreateFeedTypeAsync(farmId,
            new CreateFeedTypeRequest { Name = "alfalfa hay", Category = FeedCategory.Forage, Unit = FeedUnit.Bale, CostPerUnit = 1m });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task CreateFeedType_NegativeCost_ReturnsValidation()
    {
        using var context = CreateContext();
        var (farmId, _) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFeedTypeAsync(farmId,
            new CreateFeedTypeRequest { Name = "Bad Feed", Category = FeedCategory.Other, Unit = FeedUnit.Kilogram, CostPerUnit = -1 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateFeedType_ChangesDetails()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateFeedTypeAsync(farmId, feedTypeId,
            new UpdateFeedTypeRequest { Name = "Alfalfa Hay Premium", Category = FeedCategory.Forage, Unit = FeedUnit.Bale, CostPerUnit = 12m });

        Assert.True(result.IsSuccess);
        Assert.Equal("Alfalfa Hay Premium", result.Value!.Name);
        Assert.Equal(FeedUnit.Bale, result.Value.Unit);
        Assert.Equal(12m, result.Value.CostPerUnit);
    }

    [Fact]
    public async Task DeleteFeedType_WithoutMovements_Succeeds()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.DeleteFeedTypeAsync(farmId, feedTypeId);

        Assert.True(result.IsSuccess);
        Assert.False(await context.FeedTypes.AnyAsync(ft => ft.Id == feedTypeId));
    }

    [Fact]
    public async Task DeleteFeedType_WithMovements_ReturnsConflict()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });

        var result = await service.DeleteFeedTypeAsync(farmId, feedTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task RecordPurchase_IncreasesStock_AndComputesTotalCost()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest
            {
                FeedTypeId = feedTypeId,
                MovementType = StockMovementType.Purchase,
                Quantity = 100,
                UnitCost = 5m,
                Supplier = "AgriSupply"
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Value!.TotalCost);
        Assert.Equal(100m, result.Value.SignedQuantity);

        var stock = await service.GetStockByFeedTypeAsync(farmId, feedTypeId);
        Assert.True(stock.IsSuccess);
        Assert.Equal(100m, stock.Value!.CurrentStock);
        Assert.Equal(500m, stock.Value.TotalCost);
    }

    [Fact]
    public async Task RecordConsumption_DecreasesStock()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 100 });
        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Consumption, Quantity = 30 });

        var stock = await service.GetStockByFeedTypeAsync(farmId, feedTypeId);
        Assert.True(stock.IsSuccess);
        Assert.Equal(70m, stock.Value!.CurrentStock);
        Assert.Equal(30m, stock.Value.QuantityConsumed);
    }

    [Fact]
    public async Task RecordConsumption_InsufficientStock_ReturnsConflict()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 10 });

        var result = await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Consumption, Quantity = 50 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task RecordNegativeAdjustment_BeyondStock_ReturnsConflict()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 20 });

        var result = await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Adjustment, Quantity = -25 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task RecordPositiveAndNegativeAdjustments_NetIntoStock()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 50 });
        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Adjustment, Quantity = -5 });
        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Adjustment, Quantity = 2 });

        var stock = await service.GetStockByFeedTypeAsync(farmId, feedTypeId);
        Assert.True(stock.IsSuccess);
        Assert.Equal(-3m, stock.Value!.NetAdjustments);
        Assert.Equal(47m, stock.Value.CurrentStock);
    }

    [Fact]
    public async Task RecordMovement_ZeroQuantity_ReturnsValidation()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 0 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task RecordMovement_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        var result = await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest
            {
                FeedTypeId = feedTypeId,
                MovementType = StockMovementType.Purchase,
                Quantity = 10,
                MovementDate = DateTime.UtcNow.AddDays(7)
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task GetMovements_Paged_NewestFirst()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest
            {
                FeedTypeId = feedTypeId,
                MovementType = StockMovementType.Purchase,
                Quantity = 100,
                MovementDate = DateTime.UtcNow.AddDays(-10)
            });

        for (var day = 5; day >= 1; day--)
        {
            await service.RecordStockMovementAsync(farmId,
                new RecordStockMovementRequest
                {
                    FeedTypeId = feedTypeId,
                    MovementType = StockMovementType.Consumption,
                    Quantity = day,
                    MovementDate = DateTime.UtcNow.AddDays(-day)
                });
        }

        var page1 = await service.GetStockMovementsAsync(farmId, feedTypeId, 1, 3);
        var page2 = await service.GetStockMovementsAsync(farmId, feedTypeId, 2, 3);

        Assert.True(page1.IsSuccess);
        Assert.Equal(6, page1.Value!.TotalCount);
        Assert.Equal(3, page1.Value.Items.Count);
        Assert.Equal(StockMovementType.Consumption, page1.Value.Items[0].MovementType);
        Assert.Equal(1m, page1.Value.Items[0].Quantity);

        Assert.True(page2.IsSuccess);
        Assert.Equal(3, page2.Value!.Items.Count);
        Assert.Equal(4m, page2.Value.Items[0].Quantity);
    }

    [Fact]
    public async Task GetStock_CoversAllFeedTypes_IncludingZeroStock()
    {
        using var context = CreateContext();
        var (farmId, feedTypeId) = await SeedFeedTypeAsync(context, name: "Alfalfa Hay");
        await SeedExtraFeedTypeAsync(context, farmId);
        var service = CreateService(context);

        await service.RecordStockMovementAsync(farmId,
            new RecordStockMovementRequest { FeedTypeId = feedTypeId, MovementType = StockMovementType.Purchase, Quantity = 40 });

        var result = await service.GetStockAsync(farmId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var withStock = result.Value.First(s => s.FeedTypeName == "Alfalfa Hay");
        Assert.Equal(40m, withStock.CurrentStock);

        var empty = result.Value.First(s => s.FeedTypeName == "Mineral Mix");
        Assert.Equal(0m, empty.CurrentStock);
    }

    private static async Task SeedExtraFeedTypeAsync(FmsDbContext context, Guid farmId)
    {
        context.FeedTypes.Add(new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Mineral Mix",
            Category = FeedCategory.Mineral,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 8m
        });
        await context.SaveChangesAsync();
    }
}
