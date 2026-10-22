using FMS.API;
using FMS.Domain.Entities;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Inventory;

/// <summary>
/// The CLI's contract: a dry run never applies a pending migration and never writes a row,
/// and <c>--apply</c> is the only path that does write. SQLite is used because the assertion
/// that a migration is still pending needs a relational provider, and because it needs no
/// server. The schema is created with <c>EnsureCreated</c> on purpose, so every migration in
/// the assembly is pending while the backfill's tables already exist.
/// </summary>
public sealed class InventoryCategoryBackfillCommandTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private FmsDbContext _context = null!;
    private Guid _farmId;
    private Guid _itemId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new FmsDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _farmId = Guid.NewGuid();
        _itemId = Guid.NewGuid();
        var account = new Account { Id = Guid.NewGuid(), Name = "Account" };
        _context.Accounts.Add(account);
        _context.Farms.Add(new Farm { Id = _farmId, AccountId = account.Id, Name = "Farm" });
        _context.InventoryItems.Add(new InventoryItem
        {
            Id = _itemId,
            FarmId = _farmId,
            Name = "Wire",
            Unit = "roll",
            Category = " Supplies "
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DryRun_DoesNotApplyPendingMigrations_AndWritesNoRows()
    {
        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync()).ToList();
        Assert.NotEmpty(pendingBefore);
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync());

        var output = new StringWriter();

        var exitCode = await InventoryCategoryBackfillCommand.RunAsync(
            new InventoryCategoryBackfillService(_context),
            new[] { InventoryCategoryBackfillCommand.CommandName },
            output);

        Assert.Equal(0, exitCode);

        // The backfill's own tables are still there, but the migrations are exactly as
        // pending as they were: the command never reached MigrateAsync().
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(pendingBefore, await _context.Database.GetPendingMigrationsAsync());

        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.InventoryCategories.CountAsync());
        var item = await _context.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == _itemId);
        Assert.Null(item.CategoryId);
        Assert.Equal(" Supplies ", item.Category);

        var text = output.ToString();
        Assert.Contains("dry run (no changes written)", text);
        Assert.Contains("categories created: 1", text);
        Assert.Contains("items linked: 1", text);
        Assert.Contains("Re-run with --apply", text);
    }

    [Fact]
    public async Task Apply_IsTheOnlyPathThatWrites()
    {
        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync()).ToList();

        // A dry run first, so the apply below is proven to do the work rather than inherit
        // changes from the dry run.
        await InventoryCategoryBackfillCommand.RunAsync(
            new InventoryCategoryBackfillService(_context),
            new[] { InventoryCategoryBackfillCommand.CommandName },
            new StringWriter());

        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.InventoryCategories.CountAsync());

        var output = new StringWriter();
        var exitCode = await InventoryCategoryBackfillCommand.RunAsync(
            new InventoryCategoryBackfillService(_context),
            new[] { InventoryCategoryBackfillCommand.CommandName, "--apply" },
            output);

        Assert.Equal(0, exitCode);
        Assert.Contains("backfill applied", output.ToString());

        _context.ChangeTracker.Clear();
        var category = await _context.InventoryCategories.SingleAsync(c => c.FarmId == _farmId);
        Assert.Equal("Supplies", category.Name);
        var item = await _context.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == _itemId);
        Assert.Equal(category.Id, item.CategoryId);
        Assert.Equal(" Supplies ", item.Category);

        // Applying the backfill is still not a migration step.
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(pendingBefore, await _context.Database.GetPendingMigrationsAsync());

        // A second apply is a no-op, so the command stays safe to re-run.
        var second = await InventoryCategoryBackfillCommand.RunAsync(
            new InventoryCategoryBackfillService(_context),
            new[] { InventoryCategoryBackfillCommand.CommandName, "--apply" },
            new StringWriter());
        Assert.Equal(0, second);
        Assert.Equal(1, await _context.InventoryCategories.CountAsync());
    }
}
