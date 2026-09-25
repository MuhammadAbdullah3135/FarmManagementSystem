using System.Text;
using FMS.Application.Animal.Import;
using FMS.Application.Employees.Import;
using FMS.Application.Farm.Export;
using FMS.Application.Finance.Import;
using FMS.Application.Import;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using FMS.Infrastructure.Farm.Export;
using FMS.Infrastructure.Import;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// What the archive actually contains: one CSV per exported entity, a manifest that
/// describes those files accurately, the byte shape the app's own reader accepts, and —
/// the property the round trip depends on — headers that are the importers' own field
/// labels rather than a look-alike.
///
/// <para>
/// The manifest assertions matter as much as the data ones: the manifest is what the
/// download page and the README both render, so a row count that disagrees with the file
/// would overstate the archive's completeness everywhere it is reported.
/// </para>
/// </summary>
public class FarmExportAssemblerTests
{
    /// <summary>The importer vocabulary for each re-importable file, keyed by file name.</summary>
    private static IImportFieldCatalog CatalogFor(string fileName) => fileName switch
    {
        "animals.csv" => AnimalImportFields.FieldCatalog,
        "employees.csv" => EmployeeImportFields.FieldCatalog,
        "inventory-items.csv" => InventoryImportFields.FieldCatalog,
        "suppliers.csv" => SupplierImportFields.FieldCatalog,
        "customers.csv" => CustomerImportFields.FieldCatalog,
        "expenses.csv" => ExpenseImportFields.FieldCatalog,
        "income-records.csv" => IncomeImportFields.FieldCatalog,
        _ => throw new InvalidOperationException($"No importer catalog for '{fileName}'")
    };

    private static async Task<(ExportSeed Seed, byte[] Archive)> ExportAsync(FarmExportTestSupport.Harness harness)
    {
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var dto = await harness.BuildAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal(FarmExportStatus.Completed, dto.Status);

        var archive = await DownloadAsync(harness, seed.FarmId);

        return (seed, archive);
    }

    private static async Task<byte[]> DownloadAsync(FarmExportTestSupport.Harness harness, Guid farmId)
    {
        var opened = await harness.Service.OpenArchiveAsync(farmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        await using var stream = await harness.Files.OpenReadAsync(opened.Value!.StoragePath);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    [Fact]
    public async Task WriteAsync_ProducesOneCsvPerExportedEntity_PlusTheManifestAndReadme()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (_, archive) = await ExportAsync(harness);

        var entries = FarmExportTestSupport.ReadEntries(archive);

        var expected = FarmExportEntities.Tables
            .Select(table => table.FileName)
            .Concat(new[]
            {
                FarmExportAssembler.ManifestFileName,
                "README.txt"
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, entries.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.All(FarmExportEntities.Tables, table => Assert.NotEmpty(table.Columns));
    }

    [Fact]
    public async Task WriteAsync_ManifestDescribesTheFilesItShips()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (seed, archive) = await ExportAsync(harness);

        var manifest = FarmExportTestSupport.ReadManifest(archive);

        Assert.Equal(seed.FarmId, manifest.FarmId);
        Assert.Equal(seed.FarmName, manifest.FarmName);
        Assert.NotEqual(default(DateTime), manifest.GeneratedAtUtc);
        Assert.Equal(FarmExportEntities.Tables.Count, manifest.Files.Count);

        Assert.Equal(
            manifest.Files.Sum(file => file.RowCount),
            manifest.TotalRowCount);

        // Every count in the manifest is the number of rows the file actually has — the
        // report and the data must not be able to disagree.
        var entries = FarmExportTestSupport.ReadEntries(archive);

        foreach (var file in manifest.Files)
        {
            Assert.True(entries.ContainsKey(file.FileName), $"{file.FileName} is described but not shipped");

            var sheet = await ReadCsvAsync(entries[file.FileName]);
            Assert.Equal(file.Columns, sheet.Headers);
            Assert.Equal(file.RowCount, sheet.Rows.Count);
        }
    }

    [Fact]
    public async Task WriteAsync_ExcludesEveryDocumentedExclusion()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (_, archive) = await ExportAsync(harness);

        var entries = FarmExportTestSupport.ReadEntries(archive);

        var excludedEntities = FarmExportEntities.Exclusions
            .Select(exclusion => exclusion.Entity)
            .ToHashSet(StringComparer.Ordinal);

        var exportedEntities = FarmExportEntities.Tables
            .Select(table => table.Entity)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(excludedEntities.Intersect(exportedEntities));
        Assert.All(
            FarmExportEntities.Exclusions,
            exclusion => Assert.Contains(exclusion.Entity, excludedEntities));
        Assert.DoesNotContain("processed-mutations.csv", entries.Keys);
        Assert.DoesNotContain("farm-health-status-snapshots.csv", entries.Keys);
    }

    [Fact]
    public async Task WriteAsync_WritesTheByteShapeTheAppsOwnReaderAccepts()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (_, archive) = await ExportAsync(harness);

        var entries = FarmExportTestSupport.ReadEntries(archive);

        // UTF-8 BOM first — the same preamble client/src/utils/export.ts writes, and the
        // one SpreadsheetReader detects — then \n line endings, never \r\n.
        foreach (var name in entries.Keys.Where(name => name.EndsWith(".csv", StringComparison.Ordinal)))
        {
            var bytes = entries[name];

            Assert.True(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{name} does not start with a UTF-8 BOM");

            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            Assert.DoesNotContain("\r\n", text);
        }

        // And the whole point of the shape: the reader parses every file.
        foreach (var name in entries.Keys.Where(name => name.EndsWith(".csv", StringComparison.Ordinal)))
        {
            var sheet = await ReadCsvAsync(entries[name]);

            Assert.NotEmpty(sheet.Headers);
        }
    }

    [Fact]
    public async Task WriteAsync_EveryReimportableHeaderMapsToItsImportersOwnField()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (_, archive) = await ExportAsync(harness);

        var manifest = FarmExportTestSupport.ReadManifest(archive);
        var entries = FarmExportTestSupport.ReadEntries(archive);

        foreach (var file in manifest.Files.Where(file => file.Reimportable))
        {
            var sheet = await ReadCsvAsync(entries[file.FileName]);
            var catalog = CatalogFor(file.FileName);

            // Exact parity with the importer's own vocabulary: same labels, same order, so
            // auto-detection maps every column with no mapping supplied.
            Assert.Equal(
                catalog.All.Select(field => field.Label),
                sheet.Headers);

            foreach (var header in sheet.Headers)
            {
                var matched = catalog.MatchHeader(header);

                Assert.True(
                    matched is not null,
                    $"{file.FileName}: the header '{header}' does not map to any importer field");
            }
        }
    }

    [Fact]
    public async Task WriteAsync_CarriesTheDisclosedLimitations()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var (_, archive) = await ExportAsync(harness);

        var entries = FarmExportTestSupport.ReadEntries(archive);
        var readme = Encoding.UTF8.GetString(entries["README.txt"]);

        Assert.Contains("Not included", readme);
        Assert.Contains("RefreshToken", readme);
        Assert.Contains("uploaded files are not included", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no duplicate rule", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_OfAnEmptyFarm_StillProducesAReadableArchive()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var farm = new FMS.Domain.Entities.Farm
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Name = "Empty Farm"
        };

        harness.Context.Farms.Add(farm);
        await harness.Context.SaveChangesAsync();

        var dto = await harness.BuildAsync(farm.Id, Guid.NewGuid());

        Assert.Equal(FarmExportStatus.Completed, dto.Status);
        Assert.Equal(0, dto.Manifest!.TotalRowCount);
        Assert.Equal(FarmExportEntities.Tables.Count, dto.Manifest.Files.Count);

        var archive = await DownloadAsync(harness, farm.Id);
        var entries = FarmExportTestSupport.ReadEntries(archive);

        // A farm with no records still gets every file, so the archive's shape does not
        // depend on the farm having data — an empty table and a missing table must not look
        // the same to whoever opens it.
        Assert.Contains("animals.csv", entries.Keys);
        Assert.Equal(0, FarmExportTestSupport.ReadManifest(archive).Files
            .Single(file => file.FileName == "animals.csv").RowCount);
    }

    private static async Task<SpreadsheetSheet> ReadCsvAsync(byte[] csv)
    {
        var reader = new SpreadsheetReader();
        var result = await reader.ReadAsync(new MemoryStream(csv), "file.csv", 100_000);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value!;
    }
}
