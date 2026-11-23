using FMS.API.Security;

namespace FMS.Domain.Tests;

/// <summary>
/// The listen port is part of the contract with the client: the Vite dev server proxies
/// <c>/api</c> to <c>http://localhost:5200</c> and nothing else.
///
/// <para>
/// <c>PORT</c> overrides every other URL setting, so a value inherited from the shell — this
/// dev box exports <c>PORT=0</c>, which asks Kestrel for a random free port — used to move the
/// API without a word: the API was healthy, the proxy pointed at nothing, and the UI showed
/// empty lists. These pin that Development refuses any other port, that the platform still
/// owns <c>PORT</c> outside Development, and that the guard stays wired into startup.
/// </para>
/// </summary>
public class ListenPortGuardTests
{
    // ── unset: the launch profile decides ─────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NoPortInDevelopment_IsLeftToTheLaunchProfile(string? port)
    {
        Assert.Null(ListenPortGuard.Validate(port, isDevelopment: true));
    }

    // ── the one value the proxy can reach ─────────────────

    [Theory]
    [InlineData("5200")]
    [InlineData(" 5200 ")]
    public void Validate_TheProxiedPort_IsAccepted(string port)
    {
        Assert.Null(ListenPortGuard.Validate(port, isDevelopment: true));
    }

    // ── every other value in Development ──────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("5000")]
    [InlineData("5201")]
    [InlineData("8080")]
    public void Validate_AnyOtherPortInDevelopment_IsRejected(string port)
    {
        Assert.NotNull(ListenPortGuard.Validate(port, isDevelopment: true));
    }

    // ── outside Development PORT belongs to the platform ──

    [Theory]
    [InlineData("0")]
    [InlineData("8080")]
    public void Validate_OutsideDevelopment_AnyPortIsAllowed(string port)
    {
        // Heroku assigns PORT and expects the dyno to honour it; the dev proxy contract
        // does not apply there, so the guard must not break that deployment.
        Assert.Null(ListenPortGuard.Validate(port, isDevelopment: false));
    }

    // ── the message an operator actually sees ─────────────

    [Fact]
    public void EnsureUsable_NamesTheEnvironmentTheValueAndTheFix()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ListenPortGuard.EnsureUsable("0", "Development", isDevelopment: true));

        Assert.Contains("Refusing to start", error.Message);
        Assert.Contains("Development", error.Message);
        Assert.Contains("'0'", error.Message);
        Assert.Contains("Vite dev server", error.Message);
        Assert.Contains("PORT=5200", error.Message);
        // The symptom, so the operator recognises it rather than hunting for a code bug.
        Assert.Contains("random", error.Message);
    }

    [Fact]
    public void EnsureUsable_AcceptableValues_DoNotThrow()
    {
        ListenPortGuard.EnsureUsable("5200", "Development", isDevelopment: true);
        ListenPortGuard.EnsureUsable(null, "Development", isDevelopment: true);
        ListenPortGuard.EnsureUsable("0", "Production", isDevelopment: false);
    }

    // ── the guard must stay wired in ──────────────────────

    [Fact]
    public void Program_ChecksThePortBeforeRegisteringServices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "FMS.API", "Program.cs"));

        var guardAt = source.IndexOf("ListenPortGuard.EnsureUsable", StringComparison.Ordinal);
        Assert.True(guardAt >= 0, "Program.cs no longer validates PORT at startup.");

        // Ahead of service registration: the host should refuse before it migrates a database
        // or seeds anything, which is most of the time a developer spends waiting on startup.
        var dbContextAt = source.IndexOf("AddDbContext<FmsDbContext>", StringComparison.Ordinal);
        Assert.True(dbContextAt > guardAt, "The listen-port guard must run before service registration.");

        // The binding itself still honours PORT; the guard only decides whether it may.
        Assert.Contains("Environment.GetEnvironmentVariable(\"PORT\")", source);
    }
}
