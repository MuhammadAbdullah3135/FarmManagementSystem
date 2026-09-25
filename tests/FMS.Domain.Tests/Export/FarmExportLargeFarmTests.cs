using System.Diagnostics;
using FMS.Domain.Entities;
using FMS.Infrastructure.Import;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The case the background design exists for: a farm with years of history rather than
/// the small fixture every other test uses.
///
/// <para>
/// Two different claims are checked separately, because they have different failure
/// modes. The <em>request</em> must not depend on the farm's size — it records the
/// request and hands it to the queue, and that is asserted against a hard budget. The
/// <em>build</em> is allowed to take as long as the data takes, because it runs outside
/// any request; what it may not do is drop rows, so the manifest is checked row for row
/// against what the seeder wrote. Both durations are reported as evidence.
/// </para>
/// </summary>
public class FarmExportLargeFarmTests
{
    private static readonly Guid Requester = Guid.NewGuid();

    private readonly ITestOutputHelper _output;

    public FarmExportLargeFarmTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Export_OfASeededFarmWithYearsOfHistory_CostsNothingInTheRequest_AndDropsNoRow()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var seeding = Stopwatch.StartNew();
        var farmId = await LargeFarmSeeder.SeedAsync(harness.Context);
        seeding.Stop();

        // ── the request ────────────────────────────────────────────────
        var request = Stopwatch.StartNew();
        var requested = await harness.Service.RequestAsync(farmId, Requester);
        request.Stop();

        Assert.True(requested.IsSuccess, requested.Error?.Message);
        var exportId = requested.Value!.Id;

        // The one claim that is structural rather than incidental: asking for the export
        // does not read the export's data. A farm with 43,000 rows and a farm with one row
        // cost the same here, which is what makes a timeout impossible rather than unlikely.
        Assert.True(
            request.ElapsedMilliseconds < 5_000,
            $"Requesting an export took {request.ElapsedMilliseconds}ms — it only records the "
                + "request and enqueue a job, so its cost must not grow with the farm.");

        var queued = harness.Queue.Enqueued.Single();
        Assert.Equal(farmId, queued.FarmId);
        Assert.Equal(exportId, queued.ExportId);

        // ── the build ──────────────────────────────────────────────────
        var build = Stopwatch.StartNew();
        await harness.Job.ExecuteAsync(queued.FarmId, queued.ExportId);
        build.Stop();

        var dto = (await harness.Service.GetLatestAsync(farmId)).Value!;
        Assert.Equal(FarmExportStatus.Completed, dto.Status);

        var manifest = dto.Manifest!;

        foreach (var (fileName, expected) in LargeFarmSeeder.ExpectedRowsByFile())
        {
            var file = manifest.Files.SingleOrDefault(candidate => candidate.FileName == fileName);

            Assert.True(file is not null, $"{fileName} is missing from the archive");
            Assert.True(
                file!.RowCount == expected,
                $"{fileName}: the seeder wrote {expected} rows but the archive carries {file.RowCount}");
        }

        // Nothing under-counts: the manifest's total is at least every business row written.
        Assert.True(
            manifest.TotalRowCount >= LargeFarmSeeder.BusinessRowCount,
            $"The manifest accounts for {manifest.TotalRowCount} rows, less than the "
                + $"{LargeFarmSeeder.BusinessRowCount} the farm holds");

        // ── the archive itself ─────────────────────────────────────────
        var opened = await harness.Service.OpenArchiveAsync(farmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        await using var stream = await harness.Files.OpenReadAsync(opened.Value!.StoragePath);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        var entries = FarmExportTestSupport.ReadEntries(buffer.ToArray());
        var archiveManifest = FarmExportTestSupport.ReadManifest(buffer.ToArray());

        Assert.Equal(manifest.TotalRowCount, archiveManifest.TotalRowCount);

        // The biggest table, read back through the app's own reader, so "the row is in the
        // manifest" is not taken on trust: the CSV really does hold them.
        var reader = new SpreadsheetReader();
        var weights = await reader.ReadAsync(
            new MemoryStream(entries["weight-records.csv"]), "weights.csv", LargeFarmSeeder.WeightRecordCount + 100);

        Assert.True(weights.IsSuccess, weights.Error?.Message);
        Assert.Equal(LargeFarmSeeder.WeightRecordCount, weights.Value!.Rows.Count);

        _output.WriteLine($"seeded rows:   {LargeFarmSeeder.BusinessRowCount} ({seeding.ElapsedMilliseconds} ms)");
        _output.WriteLine($"request:       {request.ElapsedMilliseconds} ms");
        _output.WriteLine($"build:         {build.ElapsedMilliseconds} ms");
        _output.WriteLine($"archive:       {buffer.Length} bytes");
        _output.WriteLine($"manifest rows: {manifest.TotalRowCount} across {manifest.Files.Count} files");
    }

    [Fact]
    public async Task Export_OfASeededFarm_ReplacesThePreviousArchiveRatherThanAccumulatingThem()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var farmId = await LargeFarmSeeder.SeedAsync(harness.Context);

        var first = await harness.BuildAsync(farmId, Requester);
        var firstArchive = first.Manifest!.TotalRowCount;

        var second = await harness.BuildAsync(farmId, Requester);

        Assert.Equal(FarmExportStatus.Completed, second.Status);
        Assert.Equal(firstArchive, second.Manifest!.TotalRowCount);

        // Exactly one archive per farm, whatever the export count: the storage cost of the
        // feature is bounded without a retention job.
        var opened = await harness.Service.OpenArchiveAsync(farmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        var archives = Directory
            .GetFiles(Path.Combine(harness.StorageRoot, "exports"), "*.zip", SearchOption.AllDirectories);

        Assert.Single(archives);
    }
}
