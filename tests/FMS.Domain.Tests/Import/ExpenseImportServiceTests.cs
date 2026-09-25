using System.Text;
using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Application.Finance.Import;
using FMS.Application.Import;
using FMS.Domain.Entities;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The expense import: column mapping, farm lookups (category, payment method, optional
/// animal tag and location), row validation against the create endpoint's own rules, and
/// the all-or-nothing commit.
///
/// <para>
/// The important one is the duplicate policy. An expense has no identifier, so the
/// importer deliberately has <b>no duplicate rule</b> — two identical rows are both
/// written, exactly as the add form would create both. That is a decision, not a gap, so
/// it is asserted here rather than left to chance.
/// </para>
/// </summary>
public class ExpenseImportServiceTests
{
    private const string FullHeaders = "date,amount,category,payment,animal,location,description";

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-15,1500.50,Feed,Cash,,Store room,Dairy meal\n" +
                  "2026-01-16,300,Feed,Cash,TAG-0001,,Vet visit\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);
        Assert.Equal(0, await harness.Context.Expenses.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFarmsLookupNames()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,,,,,,\n"), "expenses.csv", null);

        var dto = preview.Value!;
        Assert.Equal(new[] { "Feed" }, dto.Lookups[ExpenseImportFields.ExpenseCategory]);
        Assert.Equal(new[] { "Cash" }, dto.Lookups[ExpenseImportFields.PaymentMethod]);
        Assert.Equal(new[] { "Store room" }, dto.Lookups[ExpenseImportFields.Location]);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryExpense_WithItsReferences()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-15,1500.50,Feed,Cash,,Store room,Dairy meal\n" +
                  "2026-01-16,300,Feed,Cash,TAG-0001,,Vet visit\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        var expenses = await harness.Context.Expenses.OrderBy(e => e.ExpenseDate).ToListAsync();
        Assert.Equal(1500.50m, expenses[0].Amount);
        Assert.Equal(harness.CategoryId, expenses[0].ExpenseCategoryId);
        Assert.Equal(harness.PaymentMethodId, expenses[0].PaymentMethodId);
        Assert.Equal(harness.LocationId, expenses[0].LocationId);
        Assert.Null(expenses[0].AnimalId);
        Assert.Equal(harness.AnimalId, expenses[1].AnimalId);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), expenses[0].ExpenseDate);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        await harness.Service.CommitAsync(
            harness.FarmId,
            Utf8($"{FullHeaders}\n2026-01-15,1500.50,Feed,Cash,,Store room,Dairy meal\n"),
            "expenses.csv", null);

        var imported = Assert.Single(await harness.Context.Expenses.ToListAsync());

        var created = await harness.Finance.CreateExpenseAsync(harness.FarmId, new CreateExpenseRequest
        {
            ExpenseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Amount = 1500.50m,
            ExpenseCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId,
            LocationId = harness.LocationId,
            Description = "Dairy meal"
        });

        Assert.True(created.IsSuccess);
        Assert.Equal(created.Value!.Amount, imported.Amount);
        Assert.Equal(created.Value.ExpenseDate, imported.ExpenseDate);
        Assert.Equal(created.Value.ExpenseCategoryId, imported.ExpenseCategoryId);
        Assert.Equal(created.Value.PaymentMethodId, imported.PaymentMethodId);
        Assert.Equal(created.Value.Description, imported.Description);
    }

    // ── the deliberate no-duplicate rule ────────────────────

    [Fact]
    public async Task Commit_IdenticalRows_AreBothWritten_BecauseAnExpenseHasNoDuplicate()
    {
        using var harness = await CreateHarnessAsync();

        // The same date, amount, category, payment method and description twice. An
        // expense has no identifier, and two such purchases are two real transactions,
        // so — unlike a supplier or an inventory item — this must import both.
        var csv = $"{FullHeaders}\n" +
                  "2026-01-15,1500.50,Feed,Cash,,,Dairy meal\n" +
                  "2026-01-15,1500.50,Feed,Cash,,,Dairy meal\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        Assert.Empty(commit.Value.InvalidRows);
        Assert.Equal(2, await harness.Context.Expenses.CountAsync());
    }

    // ── farm lookups ────────────────────────────────────────

    [Fact]
    public async Task Commit_UnknownCategory_IsRefusedWithTheFilesValue_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n2026-01-15,100,Nonexistent,Cash,,,x\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        var message = Assert.Single(Assert.Single(commit.Value.InvalidRows).Errors).Message;
        Assert.Contains("Nonexistent", message);
        Assert.Contains("was not found in this farm", message);
        Assert.Equal(0, await harness.Context.Expenses.CountAsync());
    }

    [Fact]
    public async Task Commit_UnknownAnimalTag_IsRefusedWithTheFilesValue()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n2026-01-15,100,Feed,Cash,GHOST,,x\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Contains("GHOST", Assert.Single(Assert.Single(commit.Value.InvalidRows).Errors).Message);
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-15,1500.50,Feed,Cash,,,Dairy meal\n" +
                  "2026-01-16,0,Feed,Cash,,,Bad\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "expenses.csv", null);

        Assert.Equal(2, commit.Value!.TotalRows);
        Assert.Equal(0, commit.Value.ImportedCount);
        Assert.Equal(0, await harness.Context.Expenses.CountAsync());
    }

    // ── byte-identical to the create endpoint ───────────────

    [Fact]
    public async Task Commit_ZeroAmount_ReportsTheSameMessageAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n2026-01-15,,Feed,Cash,,,x\n"), "expenses.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateExpenseAsync(harness.FarmId, new CreateExpenseRequest
        {
            Amount = 0,
            ExpenseCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
        Assert.Equal("Amount must be greater than zero", message);
    }

    [Fact]
    public async Task Commit_FutureDate_ReportsTheSameMessageAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();
        var future = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n2099-01-01,100,Feed,Cash,,,x\n"), "expenses.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateExpenseAsync(harness.FarmId, new CreateExpenseRequest
        {
            ExpenseDate = future,
            Amount = 100,
            ExpenseCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
        Assert.Contains("cannot be in the future", message);
    }

    [Fact]
    public async Task Commit_OverlongDescription_ReportsTheSameCapAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();
        var longDescription = new string('d', ExpenseRules.DescriptionMaxLength + 1);

        var commit = await harness.Service.CommitAsync(
            harness.FarmId,
            Utf8($"{FullHeaders}\n2026-01-15,100,Feed,Cash,,,{longDescription}\n"),
            "expenses.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateExpenseAsync(harness.FarmId, new CreateExpenseRequest
        {
            Amount = 100,
            ExpenseCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId,
            Description = longDescription
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
        Assert.Contains("cannot exceed", message);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n"), "expenses.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("no data rows", preview.Error!.Message);
    }

    // ── harness ─────────────────────────────────────────────

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Guid FarmId { get; init; }
        public required Guid CategoryId { get; init; }
        public required Guid PaymentMethodId { get; init; }
        public required Guid LocationId { get; init; }
        public required Guid AnimalId { get; init; }
        public required ExpenseImportService Service { get; init; }
        public required IFinanceService Finance { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }

        public void Dispose() => Context.Dispose();
    }

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<Harness> CreateHarnessAsync(Action<ImportOptions>? configure = null)
    {
        var context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Import Farm" };
        var category = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        var method = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Store room" };
        var animal = new Animal { Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "TAG-0001" };

        context.Farms.Add(farm);
        context.ExpenseCategories.Add(category);
        context.PaymentMethods.Add(method);
        context.Locations.Add(location);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        var (logger, logs) = TestLoggers.Create<ExpenseImportService>();
        var finance = new FinanceService(context, new FixedCurrentUser());

        var options = new ImportOptions();
        configure?.Invoke(options);

        return new Harness
        {
            Context = context,
            FarmId = farm.Id,
            CategoryId = category.Id,
            PaymentMethodId = method.Id,
            LocationId = location.Id,
            AnimalId = animal.Id,
            Service = new ExpenseImportService(
                new SpreadsheetReader(), finance, context, Options.Create(options), logger),
            Finance = finance,
            Logs = logs
        };
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
