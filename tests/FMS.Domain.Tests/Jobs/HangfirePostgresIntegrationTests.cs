using FMS.API.Jobs;
using FMS.Application.Jobs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// The other half of <see cref="BackgroundJobsSetupTests"/>, which can only reach
/// the container wiring because "the job store is PostgreSQL, which the InMemory
/// test provider cannot stand in for".
///
/// This exercises the storage path those tests could not: Hangfire's schema is
/// installed into a real PostgreSQL database and the three recurring jobs are
/// written to and read back from it. That is the one part of the scheduler that
/// has never been proven anywhere — a wrong schema name, a storage provider that
/// quietly no-ops, or an id that collides on restart would all have been
/// discoverable only in production.
///
/// Connection: the FMS_TEST_POSTGRES environment variable, falling back to
/// Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres.
/// If no server is reachable the tests SKIP with a clear message, exactly like
/// DashboardPostgresIntegrationTests, so the suite stays usable without a database.
/// </summary>
public class HangfirePostgresIntegrationTests : IClassFixture<HangfirePostgresIntegrationTests.PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _database;

    public HangfirePostgresIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// Creates a throwaway database per run (dropped on dispose) so a real
    /// PostgreSQL server can be used without touching its existing data.
    /// </summary>
    public class PostgresDatabaseFixture : IDisposable
    {
        private readonly string _adminConnectionString;
        private string? _testDbName;

        public PostgresDatabaseFixture()
        {
            var raw = Environment.GetEnvironmentVariable("FMS_TEST_POSTGRES")
                ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

            _adminConnectionString = new NpgsqlConnectionStringBuilder(raw)
            {
                Timeout = 3 // fail fast when no server is reachable
            }.ConnectionString;

            UnavailableReason = ProbeAndPrepareAsync().GetAwaiter().GetResult();
        }

        /// <summary>Non-null → PostgreSQL unreachable; tests skip with this message.</summary>
        public string? UnavailableReason { get; private set; }

        public string ConnectionString =>
            new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _testDbName }.ConnectionString;

        private async Task<string?> ProbeAndPrepareAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_adminConnectionString);
                await conn.OpenAsync();

                _testDbName = $"fms_hangfire_test_{Guid.NewGuid():N}";

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE {_testDbName}";
                await cmd.ExecuteNonQueryAsync();

                return null;
            }
            catch (Exception ex)
            {
                return "Hangfire PostgreSQL integration tests skipped: no PostgreSQL server reachable using "
                    + "FMS_TEST_POSTGRES (or the default Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres). "
                    + $"Set FMS_TEST_POSTGRES to a writable PostgreSQL server to run them. Underlying error: {ex.Message}";
            }
        }

        /// <summary>
        /// Rows in Hangfire's own bookkeeping table, read over raw SQL and keyed by
        /// the column names the server reports.
        ///
        /// Hangfire owns this table, so its columns are read rather than assumed —
        /// quoting "Key"/"Value" (as this first did) failed with
        /// 42703: column "Key" does not exist the moment the query ran for real,
        /// because Hangfire.PostgreSql creates them lowercase.
        /// </summary>
        public List<Dictionary<string, string?>> QueryHangfireSet()
        {
            var rows = new List<Dictionary<string, string?>>();
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            // "set" is quoted because SET is a reserved word; this table is where
            // Hangfire.PostgreSql keeps its key/value sets, including recurring jobs.
            cmd.CommandText = "SELECT * FROM hangfire.\"set\"";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                }

                rows.Add(row);
            }

            return rows;
        }

        public List<string> HangfireTables()
        {
            var tables = new List<string>();
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'hangfire' ORDER BY table_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        public bool HangfireSchemaExists()
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'hangfire'";
            return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
        }

        public void Dispose()
        {
            if (_testDbName is null)
            {
                return;
            }

            try
            {
                using var conn = new NpgsqlConnection(_adminConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS {_testDbName} WITH (FORCE)";
                cmd.ExecuteNonQuery();
            }
            catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Builds the scheduler exactly as the API does: the real
    /// <see cref="BackgroundJobsSetup.AddFmsBackgroundJobs"/> against the test
    /// database, with no InMemory substitute anywhere.
    /// </summary>
    private ServiceProvider BuildScheduler(out JobOptions options)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _database.ConnectionString,
                ["Jobs:Enabled"] = "true",
                ["Jobs:FeedingTaskGeneration:Enabled"] = "true",
                ["Jobs:FeedingTaskGeneration:Cron"] = "5 0 * * *",
                ["Jobs:HealthStatusRecalculation:Enabled"] = "true",
                ["Jobs:HealthStatusRecalculation:Cron"] = "0 * * * *",
                ["Jobs:NotificationDispatch:Enabled"] = "true",
                ["Jobs:NotificationDispatch:Cron"] = "*/15 * * * *"
            })
            .Build();

        options = BackgroundJobsSetup.ResolveOptions(configuration);

        return SchedulerTestProvider.Build(configuration, options);
    }

    private static bool Holds(Dictionary<string, string?> row, string value) =>
        row.Values.Any(column => column is not null && column.Contains(value, StringComparison.Ordinal));

    private void SkipIfUnavailable()
    {
        Skip.If(_database.UnavailableReason is not null, _database.UnavailableReason);
    }

    [SkippableFact]
    public async Task Initialize_OnRealPostgres_InstallsTheHangfireSchema()
    {
        SkipIfUnavailable();

        // Not disposed on purpose — see SchedulerTestProvider.
        var provider = BuildScheduler(out var options);
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        // Deliberately no assertion about the schema's absence beforehand: the
        // tests in this class share one database and xUnit does not promise an
        // order, so a pre-condition would make this test depend on its siblings.
        BackgroundJobsSetup.Initialize(provider, options, loggerFactory.CreateLogger("HangfirePostgresIntegrationTests"));

        Assert.True(_database.HangfireSchemaExists(),
            "BackgroundJobsSetup.Initialize must install the hangfire schema (SchemaName = \"hangfire\").");

        var tables = _database.HangfireTables();
        Assert.Contains("job", tables);
        Assert.Contains("state", tables);
        Assert.Contains("set", tables);

        await Task.CompletedTask;
    }

    [SkippableFact]
    public async Task Initialize_OnRealPostgres_RegistersTheThreeRecurringJobs()
    {
        SkipIfUnavailable();

        var provider = BuildScheduler(out var options);
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("HangfirePostgresIntegrationTests");

        BackgroundJobsSetup.Initialize(provider, options, logger);

        // Read through the application's own status provider: this is exactly the
        // view a SystemOwner gets from GET /api/admin/jobs, so exercising it here
        // covers the endpoint's data source against a real job store too.
        var status = await provider.GetRequiredService<IJobStatusProvider>().GetStatusAsync();

        Assert.True(status.Enabled);
        Assert.Empty(status.Warnings);
        Assert.Equal(3, status.RecurringJobs.Count);

        Assert.Equal("5 0 * * *",
            Assert.Single(status.RecurringJobs, j => j.Id == JobNames.FeedingTaskGenerationFanOut).Cron);
        Assert.Equal("0 * * * *",
            Assert.Single(status.RecurringJobs, j => j.Id == JobNames.HealthStatusRecalculationFanOut).Cron);
        Assert.Equal("*/15 * * * *",
            Assert.Single(status.RecurringJobs, j => j.Id == JobNames.NotificationDispatchFanOut).Cron);

        // Every recurring job must have a next run: a registered id with no
        // schedule is a silently dead job.
        Assert.All(status.RecurringJobs, job => Assert.NotNull(job.NextRunUtc));

        // Independent cross-check straight from the storage connection, so the
        // assertion does not rest solely on the provider's rendering of it.
        using var connection = provider.GetRequiredService<JobStorage>().GetConnection();
        var ids = connection.GetRecurringJobs()
            .Select(job => job.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                JobNames.FeedingTaskGenerationFanOut,
                JobNames.HealthStatusRecalculationFanOut,
                JobNames.NotificationDispatchFanOut
            }.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            ids);

        // Cross-check through raw SQL, so the assertion does not rest solely on
        // the monitoring API's view of a table it wrote itself. Key/value shape
        // is left to Hangfire: the ids just have to be in there.
        var rows = _database.QueryHangfireSet();

        // Which column carries the id is Hangfire's business; that the id is in one
        // of them is the point of reading the table directly.
        Assert.Contains(rows, row => Holds(row, JobNames.FeedingTaskGenerationFanOut));
        Assert.Contains(rows, row => Holds(row, JobNames.HealthStatusRecalculationFanOut));
        Assert.Contains(rows, row => Holds(row, JobNames.NotificationDispatchFanOut));

        await Task.CompletedTask;
    }

    [SkippableFact]
    public async Task Initialize_OnRealPostgres_IsIdempotentAcrossRestarts()
    {
        SkipIfUnavailable();

        var first = BuildScheduler(out var firstOptions);
        BackgroundJobsSetup.Initialize(
            first,
            firstOptions,
            first.GetRequiredService<ILoggerFactory>().CreateLogger("restart-1"));

        // A restart registers with the same ids rather than adding a second copy.
        var second = BuildScheduler(out var secondOptions);
        BackgroundJobsSetup.Initialize(
            second,
            secondOptions,
            second.GetRequiredService<ILoggerFactory>().CreateLogger("restart-2"));

        var status = await second.GetRequiredService<IJobStatusProvider>().GetStatusAsync();

        // More registrations of the same three ids than there are ids by now (this
        // class shares one database), so a duplicate-per-restart bug would show up
        // as more than three entries here.
        Assert.Equal(3, status.RecurringJobs.Count);
        Assert.Empty(status.Warnings);
    }
}
