using System.Diagnostics;
using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Health;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// The same round-trip counts as <see cref="ReportQueryCountTests"/>, measured against a real
/// PostgreSQL server, plus the timings that only a real server can give.
///
/// <para>
/// Two different claims live here. The <em>counts</em> are the same numbers SQLite asserts, which
/// makes them a property of the code rather than of a provider: if PostgreSQL disagreed, the
/// batched loads would be translating into something with a different shape. The <em>durations</em>
/// are the only honest before/after wall-clock figures available, because InMemory evaluates LINQ
/// client-side and so times nothing that a deployment would run.
/// </para>
///
/// <para>
/// Skips with a clear message when no server is reachable, exactly as the other Postgres
/// integration tests do, so the suite stays usable without a database. Runbook in
/// <c>docs/VERIFICATION.md</c>.
/// </para>
/// </summary>
public class ReportQueryCountPostgresTests : IDisposable
{
    private const int AnimalCount = 500;

    private readonly ITestOutputHelper _output;
    private readonly string _adminConnectionString;
    private readonly string? _unavailableReason;
    private readonly string? _databaseName;

    public ReportQueryCountPostgresTests(ITestOutputHelper output)
    {
        _output = output;

        var raw = Environment.GetEnvironmentVariable("FMS_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        _adminConnectionString = new NpgsqlConnectionStringBuilder(raw) { Timeout = 3 }.ConnectionString;

        try
        {
            using var connection = new NpgsqlConnection(_adminConnectionString);
            connection.Open();

            _databaseName = $"fms_perf_{Guid.NewGuid():N}";

            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {_databaseName}";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _unavailableReason =
                "PostgreSQL report-count tests skipped: no PostgreSQL server reachable using "
                + "FMS_TEST_POSTGRES (or the default localhost). Set FMS_TEST_POSTGRES to a "
                + $"writable server to run them. Underlying error: {ex.Message}";
        }
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    [SkippableFact]
    public async Task EveryRewrittenPath_IssuesTheSameHandfulOfQueries_OnRealPostgres()
    {
        Skip.If(_unavailableReason is not null, _unavailableReason);

        var measured = new CountingCommandInterceptor();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(_adminConnectionString)
            {
                Database = _databaseName
            }.ConnectionString)
            .AddInterceptors(measured)
            .Options;

        await using var context = new FmsDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var seed = Stopwatch.StartNew();
        var fixture = await ReportPerfFixture.SeedAsync(context, AnimalCount);
        seed.Stop();

        var user = new FixedCurrentUser();
        var reportService = new ReportService(context);
        var vaccineService = new VaccineService(context, user);
        var weightCheckService = new WeightCheckScheduleService(context, user);
        var feedService = new FeedService(context, user);

        measured.Reset();
        var reportTimer = Stopwatch.StartNew();
        await reportService.GetVaccinationReportAsync(fixture.FarmId, null, null);
        reportTimer.Stop();
        var reportReaders = measured.Readers;

        measured.Reset();
        var statusTimer = Stopwatch.StartNew();
        await vaccineService.GetVaccinationStatusAsync(fixture.FarmId);
        statusTimer.Stop();
        var statusReaders = measured.Readers;

        measured.Reset();
        var weightTimer = Stopwatch.StartNew();
        await weightCheckService.GetWeightCheckStatusAsync(fixture.FarmId);
        weightTimer.Stop();
        var weightReaders = measured.Readers;
        var weightWriters = measured.Writers;

        measured.Reset();
        var feedTimer = Stopwatch.StartNew();
        await feedService.GenerateTasksAsync(
            fixture.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });
        feedTimer.Stop();
        var feedReaders = measured.Readers;

        _output.WriteLine($"seed:            {seed.ElapsedMilliseconds} ms for {AnimalCount} animals");
        _output.WriteLine(
            $"vaccination report: {reportReaders} queries, {reportTimer.ElapsedMilliseconds} ms "
            + $"({fixture.VaccinationPairs} pairs)");
        _output.WriteLine(
            $"vaccination status: {statusReaders} queries, {statusTimer.ElapsedMilliseconds} ms "
            + $"({fixture.VaccinationPairs} pairs)");
        _output.WriteLine(
            $"weight check status: {weightReaders} queries, {weightWriters} writes, "
            + $"{weightTimer.ElapsedMilliseconds} ms ({fixture.WeightCheckPairs} pairs)");
        _output.WriteLine($"feeding tasks:      {feedReaders} queries, {feedTimer.ElapsedMilliseconds} ms");

        // The same constants the SQLite test asserts. Agreement across two providers is the point:
        // it makes the count a property of the code and not of whichever database ran the test.
        // Before the rewrite these were 3 + pairs, 2 + pairs, and 2 + pairs + due/overdue — at this
        // size, 1,003 / 1,002 / and over 1,800 respectively.
        Assert.Equal(4, reportReaders);
        Assert.Equal(3, statusReaders);
        Assert.Equal(4, weightReaders);
        Assert.Equal(5, feedReaders);

        // And nothing scales with the farm.
        Assert.True(reportReaders < AnimalCount);
    }

    public void Dispose()
    {
        if (_databaseName is null)
            return;

        try
        {
            using var connection = new NpgsqlConnection(_adminConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)";
            command.ExecuteNonQuery();
        }
        catch
        {
            // A leftover throwaway database is not worth failing a test run over.
        }
    }
}
