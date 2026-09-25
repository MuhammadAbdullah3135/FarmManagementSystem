using System.Text;
using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The supplier import: column mapping, row validation against the create endpoint's own
/// rules, duplicate handling and the all-or-nothing commit. The production path is the
/// same code as the animal, employee and inventory importers, so these tests focus on
/// what makes a supplier a supplier — the name conflict — and on the parity property:
/// the import says exactly what the create endpoint says.
/// </summary>
public class SupplierImportServiceTests
{
    private const string FullHeaders = "supplier,contact,products";

    // ── the happy path ──────────────────────────────────────

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Kilimo Feeds,+254 700 000000,Feed\n" +
                  "Vet Supplies Ltd,,Vet supplies\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        Assert.True(preview.IsSuccess);
        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(2, dto.ValidRowCount);
        Assert.Equal(0, dto.InvalidRowCount);
        Assert.Empty(dto.InvalidRows);

        // A preview is read-only.
        Assert.Equal(0, await harness.Context.Suppliers.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFilesHeaders_AndTheSuggestedMapping_WithNoLookups()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,,\n"), "suppliers.csv", null);

        var dto = preview.Value!;
        Assert.Equal(FullHeaders.Split(','), dto.Headers);
        Assert.Equal(0, dto.SuggestedMapping.For(SupplierImportFields.Name)!.Column);
        Assert.Equal(1, dto.SuggestedMapping.For(SupplierImportFields.ContactInfo)!.Column);

        // A supplier resolves nothing against farm configuration.
        Assert.Empty(dto.Lookups);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEverySupplier()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Kilimo Feeds,+254 700 000000,Feed\n" +
                  "Vet Supplies Ltd,,Vet supplies\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        Assert.Empty(commit.Value.InvalidRows);

        var suppliers = await harness.Context.Suppliers.OrderBy(s => s.Name).ToListAsync();
        Assert.Equal(new[] { "Kilimo Feeds", "Vet Supplies Ltd" }, suppliers.Select(s => s.Name));
        Assert.Equal("+254 700 000000", suppliers[0].ContactInfo);
        Assert.Equal("Feed", suppliers[0].ProductsSupplied);
        // A blank blank-ish cell is stored as null, exactly as the endpoint cleans it.
        Assert.Null(suppliers[1].ContactInfo);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        // The import path.
        var csv = $"{FullHeaders}\nImported Name,  contact info  ,  products  \n";
        await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);
        var imported = Assert.Single(await harness.Context.Suppliers.Where(s => s.Name == "Imported Name").ToListAsync());

        // The single-create path, same values — in another farm, because the name is the
        // identifier and the endpoint would (correctly) refuse it twice in one farm.
        var created = await harness.Suppliers.CreateSupplierAsync(await SeedForeignFarmAsync(harness), new CreateSupplierRequest
        {
            Name = "Imported Name",
            ContactInfo = "  contact info  ",
            ProductsSupplied = "  products  "
        });

        Assert.True(created.IsSuccess);
        Assert.Equal(created.Value!.Name, imported.Name);
        Assert.Equal(created.Value.ContactInfo, imported.ContactInfo);
        Assert.Equal(created.Value.ProductsSupplied, imported.ProductsSupplied);
    }

    // ── the name is the identifier ──────────────────────────

    [Fact]
    public async Task Commit_DuplicateName_IsRefusedRatherThanSkipped()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingSupplierAsync(harness, "Kilimo Feeds");

        var csv = $"{FullHeaders}\nKilimo Feeds,,Feed\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(SupplierRules.DuplicateNameMessage,
            Assert.Single(Assert.Single(commit.Value.InvalidRows).Errors).Message);

        // Still one supplier: the duplicate was refused, not merged and not skipped.
        Assert.Equal(1, await harness.Context.Suppliers.CountAsync());
    }

    [Fact]
    public async Task Commit_DuplicateNameInsideTheFile_IsRefused_AndNamesTheEarlierRow()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nKilimo Feeds,,Feed\nKilimo Feeds,,Other\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        var invalid = Assert.Single(commit.Value.InvalidRows);
        Assert.Equal(3, invalid.RowNumber);
        Assert.Contains("Duplicate supplier name 'Kilimo Feeds' in this file", invalid.Errors[0].Message);
        Assert.Contains("row 2", invalid.Errors[0].Message);

        // All-or-nothing: the first row was not written either.
        Assert.Equal(0, await harness.Context.Suppliers.CountAsync());
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        // The second row has no name.
        var csv = $"{FullHeaders}\nKilimo Feeds,,Feed\n,Vet supplies\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        Assert.Equal(2, commit.Value!.TotalRows);
        Assert.Equal(0, commit.Value.ImportedCount);

        var invalid = Assert.Single(commit.Value.InvalidRows);
        Assert.Equal(3, invalid.RowNumber);
        Assert.Null(await harness.Context.Suppliers.FirstOrDefaultAsync(s => s.Name == "Kilimo Feeds"));
    }

    [Fact]
    public async Task Commit_DuplicateInAnotherFarm_IsAllowed()
    {
        using var harness = await CreateHarnessAsync();

        var otherFarm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Other Farm" };
        harness.Context.Farms.Add(otherFarm);
        await harness.Context.SaveChangesAsync();
        await SeedExistingSupplierAsync(harness, "Kilimo Feeds", otherFarm.Id);

        var csv = $"{FullHeaders}\nKilimo Feeds,,Feed\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        // Names are unique per farm.
        Assert.Equal(1, commit.Value!.ImportedCount);
    }

    // ── byte-identical to the create endpoint ───────────────

    [Theory]
    [InlineData("", "Supplier name is required")]
    [InlineData("   ", "Supplier name is required")]
    public async Task Commit_ReportsTheEndpointsOwnMessages(string name, string expected)
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n\"{name}\",,\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);

        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;
        Assert.Equal(expected, message);

        // The endpoint, for the same input.
        var endpoint = await harness.Suppliers.CreateSupplierAsync(harness.FarmId, new CreateSupplierRequest { Name = name });
        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
    }

    [Fact]
    public async Task Commit_OverlongName_ReportsTheSameCapAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();
        var longName = new string('a', SupplierRules.NameMaxLength + 1);

        var csv = $"{FullHeaders}\n{longName},,\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "suppliers.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Suppliers.CreateSupplierAsync(
            harness.FarmId, new CreateSupplierRequest { Name = longName });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
        Assert.Contains("cannot exceed", message);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n"), "suppliers.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("no data rows", preview.Error!.Message);
    }

    // ── harness ─────────────────────────────────────────────

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Guid FarmId { get; init; }
        public required SupplierImportService Service { get; init; }
        public required ISupplierService Suppliers { get; init; }
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
        context.Farms.Add(farm);
        await context.SaveChangesAsync();

        var (logger, logs) = TestLoggers.Create<SupplierImportService>();
        var suppliers = new SupplierService(context, new FixedCurrentUser());

        var options = new ImportOptions();
        configure?.Invoke(options);

        return new Harness
        {
            Context = context,
            FarmId = farm.Id,
            Service = new SupplierImportService(new SpreadsheetReader(), suppliers, Options.Create(options), logger),
            Suppliers = suppliers,
            Logs = logs
        };
    }

    private static async Task<Guid> SeedForeignFarmAsync(Harness harness)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Foreign Farm" };
        harness.Context.Farms.Add(farm);
        await harness.Context.SaveChangesAsync();
        return farm.Id;
    }

    private static async Task SeedExistingSupplierAsync(Harness harness, string name, Guid? farmId = null)
    {
        harness.Context.Suppliers.Add(new Supplier
        {
            Id = Guid.NewGuid(),
            FarmId = farmId ?? harness.FarmId,
            Name = name
        });

        await harness.Context.SaveChangesAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
