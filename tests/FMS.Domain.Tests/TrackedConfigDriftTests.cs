using System.Text.Json;
using FMS.API.Security;
using Npgsql;

namespace FMS.Domain.Tests;

/// <summary>
/// Tripwires for a drift class that has already bitten this repository twice:
/// the application moved to PostgreSQL (Npgsql) while deployment and default
/// configuration stayed shaped for SQL Server.
///
/// Two symptoms, both invisible to `dotnet test` until now:
///   * <c>docker-compose.yml</c> ran an SQL Server container and passed
///     <c>User=sa;Password=…;TrustServerCertificate=true</c> — Npgsql accepts
///     <c>Username</c>/<c>User Id</c>/<c>UID</c> but not <c>User</c>, so the API
///     could not even parse it, let alone connect;
///   * <c>appsettings.json</c> shipped a SQL Server-shaped default connection
///     string and the SQL-Server-era JWT placeholder.
///
/// These assert the shape of committed configuration, which is exactly the thing
/// that could not be verified by running the app here (no Docker, no PostgreSQL).
/// </summary>
public class TrackedConfigDriftTests
{
    // ── helpers ───────────────────────────────────────────

    private static string RepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected to find {path}");

        return path;
    }

    private static JsonElement AppSettings(params string[] relativeParts)
    {
        var path = RepoFile(new[] { "src", "FMS.API" }.Concat(relativeParts).ToArray());
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    /// <summary>
    /// Pulls the <c>ConnectionStrings__DefaultConnection=…</c> value the
    /// api service hands to the container, with the compose variable
    /// substituted for a placeholder password.
    /// </summary>
    private static string ComposeApiConnectionString()
    {
        var line = File.ReadLines(RepoFile("docker-compose.yml"))
            .FirstOrDefault(l => l.Contains("ConnectionStrings__DefaultConnection="));

        Assert.False(string.IsNullOrWhiteSpace(line),
            "docker-compose.yml no longer sets ConnectionStrings__DefaultConnection for the api service.");

        var value = line!.Split('=', 2)[1].Trim();
        return value.Replace("${DB_PASSWORD}", "compose-test-password");
    }

    // ── the app and its deployment must agree on the provider ──

    [Fact]
    public void DockerCompose_ApiConnectionString_IsParsableByNpgsql()
    {
        var builder = new NpgsqlConnectionStringBuilder(ComposeApiConnectionString());

        Assert.False(string.IsNullOrWhiteSpace(builder.Host));
        Assert.False(string.IsNullOrWhiteSpace(builder.Username));
        Assert.False(string.IsNullOrWhiteSpace(builder.Password));
        Assert.False(string.IsNullOrWhiteSpace(builder.Database));
    }

    [Fact]
    public void DockerCompose_DatabaseService_IsPostgres()
    {
        var text = File.ReadAllText(RepoFile("docker-compose.yml"));

        Assert.DoesNotContain("mssql", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("postgres", text, StringComparison.OrdinalIgnoreCase);
        // pg_isready is the Postgres equivalent of the SQL Server sqlcmd health
        // check: without a working healthcheck the api never starts, because it
        // depends on `condition: service_healthy`.
        Assert.Contains("pg_isready", text);
    }

    [Fact]
    public void DockerCompose_Password_ComesFromTheEnvironment()
    {
        var line = File.ReadLines(RepoFile("docker-compose.yml"))
            .First(l => l.Contains("ConnectionStrings__DefaultConnection="));

        Assert.Contains("Password=${DB_PASSWORD}", line);
    }

    [Fact]
    public void AppSettings_DefaultConnection_IsParsableByNpgsql()
    {
        var raw = AppSettings("appsettings.json")
            .GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();

        var builder = new NpgsqlConnectionStringBuilder(raw);

        Assert.False(string.IsNullOrWhiteSpace(builder.Host));
        Assert.False(string.IsNullOrWhiteSpace(builder.Database));
    }

    // ── the signing key must not fall back to a committed value ──

    [Fact]
    public void AppSettings_Base_DoesNotCarryASigningKey()
    {
        var jwt = AppSettings("appsettings.json").GetProperty("Jwt");

        // Absent or empty: either way, a deployment must supply Jwt__SecretKey
        // rather than silently inherit a key that is public in this repository.
        Assert.True(
            !jwt.TryGetProperty("SecretKey", out var secretKey)
            || string.IsNullOrWhiteSpace(secretKey.GetString()),
            "appsettings.json must not define Jwt:SecretKey — production has to supply it "
            + "via the Jwt__SecretKey environment variable.");
    }

    [Fact]
    public void AppSettings_Development_SigningKey_MatchesTheGuardsPlaceholder()
    {
        var configured = AppSettings("appsettings.Development.json")
            .GetProperty("Jwt").GetProperty("SecretKey").GetString();

        // The guard rejects this exact value outside Development, so if the two
        // ever drift apart the guard would start rejecting a key it should allow
        // (or worse, allow a key it should reject).
        Assert.Equal(JwtSigningKeyGuard.DevelopmentPlaceholderKey, configured);
    }

    [Fact]
    public void AppSettings_Production_And_Staging_DoNotCarryASigningKey()
    {
        foreach (var environment in new[] { "appsettings.Production.json", "appsettings.Staging.json" })
        {
            if (!AppSettings(environment).TryGetProperty("Jwt", out var jwt))
            {
                continue; // no Jwt section at all: nothing to inherit, nothing to leak
            }

            if (!jwt.TryGetProperty("SecretKey", out var secretKey))
            {
                continue;
            }

            Assert.True(string.IsNullOrWhiteSpace(secretKey.GetString()),
                $"{environment} must not define Jwt:SecretKey.");
        }
    }
}
