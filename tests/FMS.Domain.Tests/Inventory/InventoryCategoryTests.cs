using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Inventory;

public class InventoryCategoryTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => null;
    }

    private static InventoryCategoryService CreateCategoryService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private static InventoryService CreateInventoryService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    [Fact]
    public async Task CreateCategory_RejectsACaseInsensitiveDuplicate()
    {
        using var context = CreateContext();
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        context.Farms.Add(farm);
        context.InventoryCategories.Add(new InventoryCategory
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Supplies"
        });
        await context.SaveChangesAsync();

        var service = CreateCategoryService(context);
        var result = await service.CreateCategoryAsync(
            farm.Id, new CreateInventoryCategoryRequest { Name = "  supplies  " });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Equal(DomainMessageKeys.InventoryCategoryNameExists, result.Error.MessageKey);
        Assert.Equal(1, await context.InventoryCategories.CountAsync());
    }

    [Fact]
    public async Task UpdateItem_WithCategoryId_WritesTheLinkedNameIntoTheRetainedString()
    {
        using var context = CreateContext();
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var category = new InventoryCategory
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Supplies"
        };
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Gloves",
            Category = "Old free text", Unit = "box"
        };
        context.Farms.Add(farm);
        context.InventoryCategories.Add(category);
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        var service = CreateInventoryService(context);
        var result = await service.UpdateItemAsync(farm.Id, item.Id, new UpdateInventoryItemRequest
        {
            Name = "Gloves",
            CategoryId = category.Id,
            Category = "ignored text",
            Unit = "box",
            ReorderLevel = 0,
            UnitCost = 0
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(category.Id, result.Value!.CategoryId);
        Assert.Equal("Supplies", result.Value.Category);

        var saved = await context.InventoryItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.Equal("Supplies", saved.Category);
    }

    [Fact]
    public async Task Backfill_IsIdempotent_AndReportsItsCounts()
    {
        using var context = CreateContext();
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var existingFeed = new InventoryCategory
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed"
        };
        context.Farms.Add(farm);
        context.InventoryCategories.Add(existingFeed);

        context.InventoryItems.AddRange(
            new InventoryItem { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Wire", Unit = "roll", Category = " Supplies " },
            new InventoryItem { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Rope", Unit = "roll", Category = "supplies" },
            new InventoryItem { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Grain", Unit = "kg", Category = "FEED" },
            new InventoryItem { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Blank", Unit = "kg", Category = "   " },
            new InventoryItem { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Null", Unit = "kg", Category = null });
        await context.SaveChangesAsync();

        var service = new InventoryCategoryBackfillService(context);

        var dryRun = await service.RunAsync(apply: false);
        Assert.False(dryRun.Applied);
        Assert.Equal(1, dryRun.CategoriesCreated);
        Assert.Equal(3, dryRun.ItemsLinked);
        Assert.Equal(2, dryRun.BlankCategoryItemsLeftNull);
        Assert.Equal(1, await context.InventoryCategories.CountAsync());

        var applied = await service.RunAsync(apply: true);
        Assert.True(applied.Applied);
        Assert.Equal(1, applied.CategoriesCreated);
        Assert.Equal(3, applied.ItemsLinked);
        Assert.Equal(2, applied.BlankCategoryItemsLeftNull);

        var categories = await context.InventoryCategories.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(2, categories.Count);
        var supplies = Assert.Single(categories, c => c.Name.Equals("Supplies", StringComparison.OrdinalIgnoreCase));
        var grain = await context.InventoryItems.SingleAsync(i => i.Name == "Grain");
        Assert.Equal(existingFeed.Id, grain.CategoryId);
        var wire = await context.InventoryItems.SingleAsync(i => i.Name == "Wire");
        Assert.Equal(supplies.Id, wire.CategoryId);

        var secondRun = await service.RunAsync(apply: true);
        Assert.Equal(0, secondRun.CategoriesCreated);
        Assert.Equal(0, secondRun.ItemsLinked);
        Assert.Equal(2, secondRun.BlankCategoryItemsLeftNull);
        Assert.Equal(2, await context.InventoryCategories.CountAsync());
    }
}
