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
/// The income import: the same pipeline and lookup shape as expenses, against the income
/// vocabulary. As with an expense there is deliberately <b>no duplicate rule</b>, because
/// an income record has no identifier — two identical sales are two real transactions, so
/// both are written.
/// </summary>
public class IncomeImportServiceTests
{
    private const string FullHeaders = "date,amount,category,payment,animal,location,description";

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-20,2500,Milk sales,Cash,,Milking shed,Morning milk\n" +
                  "2026-01-21,900,Milk sales,Cash,TAG-0001,,Evening milk\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "income.csv", null);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);
        Assert.Equal(0, await harness.Context.IncomeRecords.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFarmsLookupNames()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,,,,,,\n"), "income.csv", null);

        var dto = preview.Value!;
        Assert.Equal(new[] { "Milk sales" }, dto.Lookups[IncomeImportFields.IncomeCategory]);
        Assert.Equal(new[] { "Cash" }, dto.Lookups[IncomeImportFields.PaymentMethod]);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryRecord_WithItsReferences()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-20,2500,Milk sales,Cash,,Milking shed,Morning milk\n" +
                  "2026-01-21,900,Milk sales,Cash,TAG-0001,,Evening milk\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "income.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        var records = await harness.Context.IncomeRecords.OrderBy(r => r.IncomeDate).ToListAsync();
        Assert.Equal(2500m, records[0].Amount);
        Assert.Equal(harness.CategoryId, records[0].IncomeCategoryId);
        Assert.Equal(harness.PaymentMethodId, records[0].PaymentMethodId);
        Assert.Equal(harness.LocationId, records[0].LocationId);
        Assert.Equal(harness.AnimalId, records[1].AnimalId);
        Assert.Equal(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), records[0].IncomeDate);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        await harness.Service.CommitAsync(
            harness.FarmId,
            Utf8($"{FullHeaders}\n2026-01-20,2500,Milk sales,Cash,,Milking shed,Morning milk\n"),
            "income.csv", null);

        var imported = Assert.Single(await harness.Context.IncomeRecords.ToListAsync());

        var created = await harness.Finance.CreateIncomeRecordAsync(harness.FarmId, new CreateIncomeRecordRequest
        {
            IncomeDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            Amount = 2500m,
            IncomeCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId,
            LocationId = harness.LocationId,
            Description = "Morning milk"
        });

        Assert.True(created.IsSuccess);
        Assert.Equal(created.Value!.Amount, imported.Amount);
        Assert.Equal(created.Value.IncomeDate, imported.IncomeDate);
        Assert.Equal(created.Value.IncomeCategoryId, imported.IncomeCategoryId);
        Assert.Equal(created.Value.PaymentMethodId, imported.PaymentMethodId);
        Assert.Equal(created.Value.Description, imported.Description);
    }

    [Fact]
    public async Task Commit_IdenticalRows_AreBothWritten_BecauseAnIncomeRecordHasNoDuplicate()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-20,2500,Milk sales,Cash,,,Morning milk\n" +
                  "2026-01-20,2500,Milk sales,Cash,,,Morning milk\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "income.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        Assert.Equal(2, await harness.Context.IncomeRecords.CountAsync());
    }

    [Fact]
    public async Task Commit_UnknownCategory_IsRefusedWithTheFilesValue_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n2026-01-20,100,Nonexistent,Cash,,,x\n"), "income.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Contains("Nonexistent", Assert.Single(Assert.Single(commit.Value.InvalidRows).Errors).Message);
        Assert.Equal(0, await harness.Context.IncomeRecords.CountAsync());
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "2026-01-20,2500,Milk sales,Cash,,,Morning milk\n" +
                  "2026-01-21,0,Milk sales,Cash,,,Bad\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "income.csv", null);

        Assert.Equal(2, commit.Value!.TotalRows);
        Assert.Equal(0, commit.Value.ImportedCount);
        Assert.Equal(0, await harness.Context.IncomeRecords.CountAsync());
    }

    [Fact]
    public async Task Commit_ZeroAmount_ReportsTheSameMessageAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n2026-01-20,,Milk sales,Cash,,,x\n"), "income.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateIncomeRecordAsync(harness.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 0,
            IncomeCategoryId = harness.CategoryId,
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

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n2099-01-01,100,Milk sales,Cash,,,x\n"), "income.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateIncomeRecordAsync(harness.FarmId, new CreateIncomeRecordRequest
        {
            IncomeDate = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Amount = 100,
            IncomeCategoryId = harness.CategoryId,
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
        var longDescription = new string('d', IncomeRules.DescriptionMaxLength + 1);

        var commit = await harness.Service.CommitAsync(
            harness.FarmId,
            Utf8($"{FullHeaders}\n2026-01-20,100,Milk sales,Cash,,,{longDescription}\n"),
            "income.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Finance.CreateIncomeRecordAsync(harness.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 100,
            IncomeCategoryId = harness.CategoryId,
            PaymentMethodId = harness.PaymentMethodId,
            Description = longDescription
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n"), "income.csv", null);

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
        public required IncomeImportService Service { get; init; }
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
        var category = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Milk sales" };
        var method = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Milking shed" };
        var animal = new Animal { Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "TAG-0001" };

        context.Farms.Add(farm);
        context.IncomeCategories.Add(category);
        context.PaymentMethods.Add(method);
        context.Locations.Add(location);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        var (logger, logs) = TestLoggers.Create<IncomeImportService>();
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
            Service = new IncomeImportService(
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
