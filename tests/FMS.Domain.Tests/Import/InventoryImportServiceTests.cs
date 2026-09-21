using System.Text;
using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The inventory import: column mapping, row validation against the create endpoint's
/// own rules, duplicate handling and the all-or-nothing commit.
///
/// Several tests compare the two paths directly — importing a row and creating the same
/// item through <see cref="IInventoryService.CreateItemAsync"/> — rather than restating
/// the expected message, because "the import says what the endpoint says" is the
/// property that has to hold, not any particular sentence.
/// </summary>
public class InventoryImportServiceTests
{
    private const string FullHeaders = "name,category,unit,quantity,reorderLevel,unitCost,location";

    // ── the happy path ──────────────────────────────────────

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Dairy meal,Feed,kg,250,50,12.50,Store room\n" +
                  "Vet wrap,Medical,roll,0,5,3.25,,\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        Assert.True(preview.IsSuccess);
        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(2, dto.ValidRowCount);
        Assert.Equal(0, dto.InvalidRowCount);
        Assert.Empty(dto.InvalidRows);
        Assert.Equal(2, dto.SampleValidRows.Count);
        Assert.False(dto.Truncated);

        // A preview is read-only.
        Assert.Equal(0, await harness.Context.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFilesHeaders_AndTheSuggestedMapping()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,,,,,,\n"), "items.csv", null);

        var dto = preview.Value!;
        Assert.Equal(FullHeaders.Split(','), dto.Headers);
        Assert.Equal(0, dto.SuggestedMapping.For(InventoryImportFields.Name)!.Column);
        Assert.Equal(3, dto.SuggestedMapping.For(InventoryImportFields.Quantity)!.Column);
        Assert.Equal(dto.SuggestedMapping.Fields.Count, dto.Mapping.Fields.Count);

        // Inventory resolves nothing against farm configuration, so a fixed value has
        // no option list to offer — that is the difference from animals and employees.
        Assert.Empty(dto.Lookups);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryItem_WithItsOpeningStockMovement()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Dairy meal,Feed,kg,250,50,12.50,Store room\n" +
                  "Vet wrap,Medical,roll,0,5,3.25,,\n";

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(2, commit.Value!.ImportedCount);
        Assert.Equal(2, commit.Value.TotalRows);
        Assert.Empty(commit.Value.InvalidRows);

        var items = await harness.Context.InventoryItems
            .Include(item => item.StockMovements)
            .OrderBy(item => item.Name)
            .ToListAsync();

        Assert.Equal(new[] { "Dairy meal", "Vet wrap" }, items.Select(item => item.Name));

        var dairy = items[0];
        Assert.Equal("Feed", dairy.Category);
        Assert.Equal("kg", dairy.Unit);
        Assert.Equal(250m, dairy.Quantity);
        Assert.Equal(50m, dairy.ReorderLevel);
        Assert.Equal(12.50m, dairy.UnitCost);
        Assert.Equal("Store room", dairy.Location);

        // Opening stock is a movement, exactly as the create endpoint records it: an
        // imported quantity that left no trace in the ledger would be invisible to
        // every stock report.
        var opening = Assert.Single(dairy.StockMovements);
        Assert.Equal(InventoryMovementType.Purchase, opening.MovementType);
        Assert.Equal(250m, opening.Quantity);
        Assert.Equal("Opening stock", opening.Reason);

        // No movement for a zero opening quantity.
        Assert.Empty(items[1].StockMovements);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        // The same item, once by hand and once from a file. The hand-made one goes into
        // a second farm, because a same-name item in the import's own farm is a
        // duplicate and correctly refused — which the next test covers.
        var otherFarmId = Guid.NewGuid();
        var byHand = await harness.Inventory.CreateItemAsync(otherFarmId, new CreateInventoryItemRequest
        {
            Name = "  Dairy meal  ",
            Category = " Feed ",
            Unit = " kg ",
            Quantity = 250,
            ReorderLevel = 50,
            UnitCost = 12.5m,
            Location = " Store room "
        });

        var csv = $"{FullHeaders}\nDairy meal,Feed,kg,250,50,12.50,Store room\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", null);
        Assert.Equal(1, commit.Value!.ImportedCount);

        Assert.True(byHand.IsSuccess);
        var hand = byHand.Value!;
        var imported = Assert.Single(await harness.Context.InventoryItems
            .Where(item => item.FarmId == harness.FarmId)
            .ToListAsync());

        // Trimming, null-ness and rounding must not differ between the two paths.
        Assert.Equal(hand.Name, imported.Name);
        Assert.Equal(hand.Category, imported.Category);
        Assert.Equal(hand.Unit, imported.Unit);
        Assert.Equal(hand.Quantity, imported.Quantity);
        Assert.Equal(hand.ReorderLevel, imported.ReorderLevel);
        Assert.Equal(hand.UnitCost, imported.UnitCost);
        Assert.Equal(hand.Location, imported.Location);
    }

    [Fact]
    public async Task Commit_ConstantMapping_AppliesTheSameValueToEveryRow()
    {
        using var harness = await CreateHarnessAsync();

        // "Everything in this file is in the store room": the mapping a user reaches for
        // when their spreadsheet has no location column.
        var mapping = new ImportMapping
        {
            Fields = new Dictionary<string, ImportFieldMap>
            {
                [InventoryImportFields.Category] = new() { Constant = "Feed" },
                [InventoryImportFields.Location] = new() { Constant = "Store room" }
            }
        };

        var csv = "name,unit,quantity\nDairy meal,kg,10\nMaize,kg,20\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", mapping);

        Assert.Equal(2, commit.Value!.ImportedCount);

        var items = await harness.Context.InventoryItems.ToListAsync();
        Assert.All(items, item => Assert.Equal("Feed", item.Category));
        Assert.All(items, item => Assert.Equal("Store room", item.Location));
    }

    [Fact]
    public async Task Preview_ExplicitlyIgnoringARequiredField_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        // The wizard sends an empty map for a field the user cleared, which means
        // "explicitly ignored" — for a required field that is a mapping error, not a
        // row error, because no row could ever be valid.
        var mapping = new ImportMapping
        {
            Fields = new Dictionary<string, ImportFieldMap> { [InventoryImportFields.Unit] = new() }
        };

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\nDairy meal,kg\n"), "items.csv", mapping);

        Assert.False(preview.IsSuccess);
        Assert.Contains("Unit", preview.Error!.Message);
        Assert.Contains("must be mapped", preview.Error.Message);
    }

    [Fact]
    public async Task Preview_ColumnBeyondTheFilesWidth_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var mapping = new ImportMapping
        {
            Fields = new Dictionary<string, ImportFieldMap>
            {
                [InventoryImportFields.Name] = new() { Column = 5 }
            }
        };

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\nDairy meal,kg\n"), "items.csv", mapping);

        Assert.False(preview.IsSuccess);
        Assert.Contains("column 6", preview.Error!.Message);
    }

    // ── all-or-nothing ──────────────────────────────────────

    [Fact]
    public async Task Commit_OneInvalidRow_WritesNothingAndReportsEveryProblem()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Dairy meal,Feed,kg,250,50,12.50,Store room\n" +
                  ",Feed,kg,10,5,2.00,\n" +          // no name
                  "Vet wrap,Medical,,5,5,3.25,\n" +  // no unit
                  "Salt,Feed,kg,-5,1,0.50,\n";       // negative quantity

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(4, commit.Value.TotalRows);
        Assert.Equal(3, commit.Value.InvalidRows.Count);
        Assert.Equal(0, await harness.Context.InventoryItems.CountAsync());

        // And the refusal is visible, not silent.
        Assert.True(harness.Logs.HasMessageContaining("Refused inventory import"));
        Assert.True(harness.Logs.HasMessageContaining("3 of 4 rows"));
    }

    [Fact]
    public async Task Preview_MixedFile_ReportsBothCountsWithoutWriting()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nDairy meal,Feed,kg,10,5,2.00,\n,Feed,kg,10,5,2.00,\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(1, dto.ValidRowCount);
        Assert.Equal(1, dto.InvalidRowCount);
        Assert.Single(dto.SampleValidRows);
        Assert.Equal(0, await harness.Context.InventoryItems.CountAsync());
    }

    // ── the duplicate rule, in every variant ────────────────

    [Fact]
    public async Task Preview_DuplicateName_ReportsExactlyWhatTheCreateEndpointReports()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingItemAsync(harness, "Dairy meal");

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\nDairy meal,kg\n"), "items.csv", null);

        var row = Assert.Single(preview.Value!.InvalidRows);
        var error = Assert.Single(row.Errors);
        Assert.Equal(InventoryImportFields.Name, error.Field);

        // A duplicate is an error, never a silent skip and never an update: the same
        // request through the create endpoint has to say the same thing.
        var endpoint = await harness.Inventory.CreateItemAsync(harness.FarmId, new CreateInventoryItemRequest
        {
            Name = "Dairy meal",
            Unit = "kg"
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal("Conflict", endpoint.Error!.Code);
        Assert.Equal(endpoint.Error.Message, error.Message);
    }

    [Fact]
    public async Task Preview_DuplicateNameWithinTheFile_FlagsTheLaterRow_AndNamesTheEarlierOne()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "name,unit\nDairy meal,kg\nVet wrap,roll\nDairy meal,bag\n";
        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        var row = Assert.Single(preview.Value!.InvalidRows);
        Assert.Equal(4, row.RowNumber);
        var error = Assert.Single(row.Errors);
        Assert.Equal(InventoryImportFields.Name, error.Field);
        Assert.Contains("Duplicate item name 'Dairy meal'", error.Message);
        Assert.Contains("row 2", error.Message);

        // And the whole file is still refused at commit: the first row alone is not
        // enough to write, because all-or-nothing means all.
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", null);
        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(0, await harness.Context.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task Preview_NameDifferingOnlyByCase_IsNotADuplicate()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingItemAsync(harness, "Dairy meal");

        // Parity with the create endpoint, whose check is `i.Name == name`, and with the
        // unique index behind it, both of which are case-sensitive.
        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\ndairy meal,kg\n"), "items.csv", null);

        Assert.Equal(1, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);

        var endpoint = await harness.Inventory.CreateItemAsync(harness.FarmId, new CreateInventoryItemRequest
        {
            Name = "dairy meal",
            Unit = "kg"
        });

        Assert.True(endpoint.IsSuccess);
    }

    [Fact]
    public async Task Preview_NameMatchedAfterTrimming_IsStillADuplicate()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingItemAsync(harness, "Dairy meal");

        // The endpoint trims before comparing, so the import must too.
        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\n  Dairy meal  ,kg\n"), "items.csv", null);

        Assert.Equal(0, preview.Value!.ValidRowCount);
        Assert.Equal(1, preview.Value.InvalidRowCount);
    }

    [Fact]
    public async Task Preview_DuplicateAgainstAnotherFarm_IsNotADuplicate()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingItemAsync(harness, "Dairy meal", farmId: Guid.NewGuid());

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\nDairy meal,kg\n"), "items.csv", null);

        Assert.Equal(1, preview.Value!.ValidRowCount);
    }

    // ── the create endpoint's own messages, from the same rules ──

    [Theory]
    [InlineData("name,unit\n,kg\n")]                     // name is required
    [InlineData("name,unit\nDairy meal\n")]              // unit is required (short row)
    public async Task Preview_MissingRequiredField_SaysWhatTheCreateEndpointSays(string csv)
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);
        var row = Assert.Single(preview.Value!.InvalidRows);

        var endpoint = await harness.Inventory.CreateItemAsync(harness.FarmId, new CreateInventoryItemRequest
        {
            Name = row.Values[InventoryImportFields.Name],
            Unit = row.Values[InventoryImportFields.Unit]
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Contains(endpoint.Error!.Message, row.Errors.Select(error => error.Message));
    }

    [Fact]
    public async Task Preview_NegativeQuantity_IsRejectedWithTheCreateEndpointsMessage()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit,quantity\nDairy meal,kg,-5\n"), "items.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(InventoryImportFields.Quantity, error.Field);
        Assert.Equal("Quantity cannot be negative", error.Message);
    }

    /// <summary>
    /// The EF column caps, which neither path checked before. PostgreSQL rejects an
    /// over-long value with an exception: in a bulk import that is a 500 in the middle
    /// of a batch instead of a row the user can fix, and on the endpoint it is a 500
    /// instead of a validation message.
    /// </summary>
    [Theory]
    [InlineData("name", "Item name", 200)]
    [InlineData("category", "Category", 100)]
    [InlineData("location", "Location", 200)]
    public async Task Preview_ValueOverTheColumnCap_IsARowErrorRatherThanADatabaseException(
        string fieldKey, string label, int maxLength)
    {
        using var harness = await CreateHarnessAsync();
        var tooLong = new string('x', maxLength + 1);

        // Every column is mapped, and only the column under test is over-long: a
        // repeated header would leave the extra cell unmapped and unnoticed.
        var row = new Dictionary<string, string>
        {
            [InventoryImportFields.Name] = "Dairy meal",
            [InventoryImportFields.Category] = "Feed",
            [InventoryImportFields.Unit] = "kg",
            [InventoryImportFields.Location] = "Store room"
        };
        row[fieldKey] = tooLong;

        var csv = "name,category,unit,location\n" +
                  $"{row[InventoryImportFields.Name]},{row[InventoryImportFields.Category]}," +
                  $"{row[InventoryImportFields.Unit]},{row[InventoryImportFields.Location]}\n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(fieldKey, error.Field);
        Assert.Equal($"{label} cannot exceed {maxLength} characters", error.Message);

        // And the endpoint refuses the same value with the same words.
        var endpoint = await harness.Inventory.CreateItemAsync(harness.FarmId, new CreateInventoryItemRequest
        {
            Name = row[InventoryImportFields.Name],
            Unit = row[InventoryImportFields.Unit],
            Category = row[InventoryImportFields.Category],
            Location = row[InventoryImportFields.Location]
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(error.Message, endpoint.Error!.Message);
    }

    [Fact]
    public async Task Commit_UnitOverTheColumnCap_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"name,unit\nDairy meal,{new string('u', 51)}\n"), "items.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(InventoryImportFields.Unit, error.Field);
        Assert.Equal("Unit cannot exceed 50 characters", error.Message);
        Assert.Equal(0, await harness.Context.InventoryItems.CountAsync());
    }

    // ── numbers ─────────────────────────────────────────────

    [Theory]
    [InlineData("250", 250)]
    [InlineData("250.5", 250.5)]
    [InlineData("12,5", 12.5)]        // comma as the decimal separator
    [InlineData("1,234", 1234)]       // comma as a thousands separator
    [InlineData("1,234.56", 1234.56)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    public async Task Commit_NumericCells_AreReadDeterministically(string cell, decimal expected)
    {
        using var harness = await CreateHarnessAsync();

        // Quoted, because a comma inside a CSV cell is a comma inside the cell only when
        // the cell is quoted — which is what a spreadsheet exported in a locale that
        // writes 12,5 does.
        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"name,unit,quantity\nDairy meal,kg,\"{cell}\"\n"), "items.csv", null);

        Assert.Equal(1, commit.Value!.ImportedCount);
        var item = await harness.Context.InventoryItems.SingleAsync();
        Assert.Equal(expected, item.Quantity);
    }

    [Fact]
    public async Task Preview_NonNumericQuantity_IsARowErrorAndNotZero()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit,quantity\nDairy meal,kg,twenty\n"), "items.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(InventoryImportFields.Quantity, error.Field);
        Assert.Contains("'twenty' is not a number", error.Message);
    }

    [Fact]
    public async Task Preview_EmptyNumericCells_AreZero_NotErrors()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\nDairy meal,Feed,kg,,,,\n"), "items.csv", null);

        Assert.Equal(1, preview.Value!.ValidRowCount);

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\nDairy meal,Feed,kg,,,,\n"), "items.csv", null);

        Assert.Equal(1, commit.Value!.ImportedCount);
        var item = Assert.Single(await harness.Context.InventoryItems.ToListAsync());
        Assert.Equal(0m, item.Quantity);
        Assert.Equal(0m, item.ReorderLevel);
        Assert.Equal(0m, item.UnitCost);
    }

    // ── the commit revalidates from the file ────────────────

    [Fact]
    public async Task Commit_RevalidatesFromTheFile_SoAnItemCreatedAfterThePreviewBlocksIt()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nDairy meal,Feed,kg,10,5,2.00,\n";
        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);
        Assert.Equal(1, preview.Value!.ValidRowCount);

        // Someone creates the same item between the preview and the commit.
        await harness.Inventory.CreateItemAsync(harness.FarmId, new CreateInventoryItemRequest
        {
            Name = "Dairy meal",
            Unit = "kg"
        });

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(0, commit.Value!.ImportedCount);
        var row = Assert.Single(commit.Value.InvalidRows);
        Assert.Equal("An inventory item with this name already exists", Assert.Single(row.Errors).Message);

        // Still exactly the one item that was created by hand.
        Assert.Equal(1, await harness.Context.InventoryItems.CountAsync());
    }

    // ── limits ──────────────────────────────────────────────

    [Fact]
    public async Task Preview_MoreRowsThanTheConfiguredLimit_IsRefused()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxRows = 2);

        var csv = "name,unit\nA,kg\nB,kg\nC,kg\n";
        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("more than 2", preview.Error!.Message);
    }

    [Fact]
    public async Task Preview_CapsTheReportedProblemRows()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxReportedRows = 1);

        var csv = "name,unit\n,kg\n,kg\n";
        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "items.csv", null);

        var dto = preview.Value!;
        Assert.Equal(2, dto.InvalidRowCount);
        Assert.Single(dto.InvalidRows);
        // Says so rather than truncating silently: the counts still cover the whole file.
        Assert.True(dto.Truncated);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("name,unit\n"), "items.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("no data rows", preview.Error!.Message);
    }

    // ── harness ─────────────────────────────────────────────

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Guid FarmId { get; init; }
        public required InventoryImportService Service { get; init; }
        public required IInventoryService Inventory { get; init; }
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

        var (logger, logs) = TestLoggers.Create<InventoryImportService>();
        var inventory = new InventoryService(context, new FixedCurrentUser());

        var options = new ImportOptions();
        configure?.Invoke(options);

        return new Harness
        {
            Context = context,
            FarmId = farm.Id,
            Service = new InventoryImportService(new SpreadsheetReader(), inventory, Options.Create(options), logger),
            Inventory = inventory,
            Logs = logs
        };
    }

    private static async Task SeedExistingItemAsync(Harness harness, string name, Guid? farmId = null)
    {
        harness.Context.InventoryItems.Add(new InventoryItem
        {
            Id = Guid.NewGuid(),
            FarmId = farmId ?? harness.FarmId,
            Name = name,
            Unit = "kg"
        });

        await harness.Context.SaveChangesAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
