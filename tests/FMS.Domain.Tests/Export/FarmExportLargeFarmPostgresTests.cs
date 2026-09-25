using System.Diagnostics;
using System.Net.Http.Json;
using FMS.Application.Farm.Export;
using FMS.Domain.Entities;
using FMS.Domain.Tests.E2E;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The large-farm claim measured against a real PostgreSQL server rather than the InMemory
/// provider.
///
/// <para>
/// InMemory evaluates LINQ client-side and writes to a dictionary, so a duration measured
/// there says nothing about the queries a deployment will actually run. This is where the
/// numbers worth quoting come from: the seed is the same realistic farm, the request and
/// the build are timed against real SQL, and the manifest is checked row for row.
/// </para>
///
/// <para>
/// Skips with a clear message when no server is reachable, exactly as the other Postgres
/// integration tests do, so the suite stays usable without a database.
/// </para>
/// </summary>
public class FarmExportLargeFarmPostgresTests : IClassFixture<FarmExportLargeFarmPostgresTests.Factory>
{
    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public FarmExportLargeFarmPostgresTests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public class Factory : PostgresIntegrationFactory
    {
        public Guid LargeFarmId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            LargeFarmId = await LargeFarmSeeder.SeedAsync(db);
        }
    }

    [SkippableFact]
    public async Task Export_OnRealPostgres_BuildsTheWholeArchiveAndTheRequestStaysFast()
    {
        Skip.If(_factory.UnavailableReason is not null, _factory.UnavailableReason);
        _ = _factory.Host;

        var farmId = _factory.LargeFarmId;
        var client = _factory.CreateAuthenticatedClient(farmId);

        var request = Stopwatch.StartNew();
        using var requested = await client.PostAsync($"/api/farm/{farmId}/export", null);
        request.Stop();

        Assert.Equal(System.Net.HttpStatusCode.Accepted, requested.StatusCode);

        var queued = await requested.Content.ReadFromJsonAsync<FarmExportDto>();
        Assert.NotNull(queued);

        _output.WriteLine($"request over HTTP: {request.ElapsedMilliseconds} ms");

        // The same bound as the in-memory test, but against a real server: the request
        // records the request and returns, it does not read the farm.
        Assert.True(
            request.ElapsedMilliseconds < 5_000,
            $"The export request took {request.ElapsedMilliseconds} ms against PostgreSQL; "
                + "it must not scale with the farm's history.");

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        var export = jobs.ExportJobs().Single(job => job.FarmId == farmId && job.ExportId == queued!.Id);

        var build = Stopwatch.StartNew();

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<FarmExportJob>();
            await job.ExecuteAsync(export.FarmId, export.ExportId);
        }

        build.Stop();

        using var status = await client.GetAsync($"/api/farm/{farmId}/export");
        Assert.Equal(System.Net.HttpStatusCode.OK, status.StatusCode);

        var dto = await status.Content.ReadFromJsonAsync<FarmExportDto>();
        Assert.NotNull(dto);
        Assert.Equal(FarmExportStatus.Completed, dto!.Status);

        foreach (var (fileName, expected) in LargeFarmSeeder.ExpectedRowsByFile())
        {
            var file = dto.Manifest!.Files.SingleOrDefault(candidate => candidate.FileName == fileName);

            Assert.True(file is not null, $"{fileName} is missing from the archive");
            Assert.True(
                file!.RowCount == expected,
                $"{fileName}: seeded {expected}, archive carries {file.RowCount}");
        }

        var download = Stopwatch.StartNew();
        using var archive = await client.GetAsync($"/api/farm/{farmId}/export/download");
        download.Stop();

        Assert.Equal(System.Net.HttpStatusCode.OK, archive.StatusCode);

        var bytes = await archive.Content.ReadAsByteArrayAsync();

        _output.WriteLine($"seeded rows:   {LargeFarmSeeder.BusinessRowCount}");
        _output.WriteLine($"build:         {build.ElapsedMilliseconds} ms");
        _output.WriteLine($"download:      {download.ElapsedMilliseconds} ms, {bytes.Length} bytes");
        _output.WriteLine($"manifest rows: {dto.Manifest!.TotalRowCount} across {dto.Manifest.Files.Count} files");

        Assert.True(bytes.Length > 0);
        Assert.True(dto.Manifest!.TotalRowCount >= LargeFarmSeeder.BusinessRowCount);
    }
}
