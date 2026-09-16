using System.Net;
using System.Net.Http.Json;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Dashboard integration tests against a real PostgreSQL server (not the EF
/// InMemory provider). These exercise the dashboard metric calculations that
/// InMemory cannot validate, and assert the degraded-metric signal stays empty
/// on a fully capable provider.
///
/// Connection: FMS_TEST_POSTGRES env var, falling back to
/// Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres
///
/// If no PostgreSQL server is reachable the tests SKIP with a clear message
/// (never fail) so the suite remains usable on machines without a database.
/// </summary>
public class DashboardPostgresIntegrationTests : IClassFixture<DashboardPostgresIntegrationTests.PostgresFactory>
{
    public class PostgresFactory : TestWebApplicationFactory, IDisposable
    {
        private readonly string _adminConnectionString;
        private string? _testDbName;
        private string? _unavailableReason;

        public PostgresFactory()
        {
            var raw = Environment.GetEnvironmentVariable("FMS_TEST_POSTGRES")
                ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

            var builder = new NpgsqlConnectionStringBuilder(raw)
            {
                Timeout = 3 // fail fast when no server is reachable
            };
            _adminConnectionString = builder.ConnectionString;

            _unavailableReason = ProbeAndPrepareAsync().GetAwaiter().GetResult();
        }

        /// <summary>Non-null → PostgreSQL unreachable; tests skip with this message.</summary>
        public string? UnavailableReason => _unavailableReason;

        /// <summary>Shadows the base property; only valid after Host was touched (tests guard via Skip).</summary>
        public new ApiSeedData.SeedIds SeedData => base.SeedData;

        protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        {
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(_adminConnectionString)
            {
                Database = _testDbName
            }.ConnectionString);
        }

        private async Task<string?> ProbeAndPrepareAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_adminConnectionString);
                await conn.OpenAsync();

                // Per-run unique database, dropped on dispose.
                _testDbName = $"fms_test_{Guid.NewGuid():N}";

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE {_testDbName}";
                await cmd.ExecuteNonQueryAsync();

                return null;
            }
            catch (Exception ex)
            {
                return "PostgreSQL integration tests skipped: no PostgreSQL server reachable using "
                    + "FMS_TEST_POSTGRES (or the default Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres). "
                    + $"Set FMS_TEST_POSTGRES to a writable PostgreSQL server to run them. Underlying error: {ex.Message}";
            }
        }

        // Re-implement IDisposable so xunit's fixture disposal (via the
        // interface) runs this cleanup, not only the base implementation.
        void IDisposable.Dispose()
        {
            if (_testDbName is not null)
            {
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

            base.Dispose();
        }
    }

    private readonly PostgresFactory _factory;

    public DashboardPostgresIntegrationTests(PostgresFactory fixture)
    {
        _factory = fixture;
    }

    private HttpClient GetClient()
    {
        Skip.If(_factory.UnavailableReason is not null, _factory.UnavailableReason);
        _ = _factory.Host; // ensure host is created and data seeded
        return _factory.CreateAuthenticatedClient(_factory.SeedData.FarmId);
    }

    [SkippableFact]
    public async Task Summary_OnRealPostgres_ComputesUpcomingBirths()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();

        Assert.NotNull(summary);
        // Seed: 1 confirmed gestation record with ExpectedDeliveryDate = UtcNow + 10 days.
        Assert.Equal(1, summary.UpcomingBirths);
        // Real PostgreSQL must compute all metrics — nothing may be degraded.
        Assert.Empty(summary.DegradedMetrics);
    }

    [SkippableFact]
    public async Task Alerts_OnRealPostgres_IncludesDueBirthAlert()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/dashboard/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var alerts = await response.Content.ReadFromJsonAsync<List<DashboardAlertResponse>>();

        Assert.NotNull(alerts);
        Assert.Contains(alerts, a => a.AlertType == "DueBirth");
    }

    // ── DTOs for deserialization ──────────────────────────

    private class DashboardSummaryResponse
    {
        public int UpcomingBirths { get; set; }
        public List<string> DegradedMetrics { get; set; } = new();
        public int DueVaccinationCount { get; set; }
        public int DueWeightCheckCount { get; set; }
    }

    private class DashboardAlertResponse
    {
        public string AlertType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
