using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// A test host backed by a real PostgreSQL server, created per fixture run and dropped on
/// dispose.
///
/// <para>
/// The rest of the suite runs on the EF InMemory provider, which evaluates LINQ client-side,
/// ignores provider type mapping <em>and ignores unique indexes</em>. Anything whose correctness
/// lives in the database — a query PostgreSQL refuses to translate, a timestamp kind it refuses
/// to compare, a duplicate row only a unique index prevents — can only be asserted here.
/// </para>
///
/// <para>
/// Connection: <c>FMS_TEST_POSTGRES</c>, falling back to
/// <c>Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres</c>.
/// If no server is reachable the tests SKIP with a clear message (never fail), so the suite
/// remains usable on machines without a database.
/// </para>
/// </summary>
public class PostgresIntegrationFactory : TestWebApplicationFactory, IDisposable
{
    private readonly string _adminConnectionString;
    private string? _testDbName;

    public PostgresIntegrationFactory()
    {
        var raw = Environment.GetEnvironmentVariable("FMS_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        var builder = new NpgsqlConnectionStringBuilder(raw)
        {
            Timeout = 3 // fail fast when no server is reachable
        };
        _adminConnectionString = builder.ConnectionString;

        UnavailableReason = ProbeAndPrepareAsync().GetAwaiter().GetResult();
    }

    /// <summary>Non-null → PostgreSQL unreachable; tests skip with this message.</summary>
    public string? UnavailableReason { get; }

    /// <summary>Shadows the base property; only valid after <c>Host</c> was touched (tests guard via Skip).</summary>
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
