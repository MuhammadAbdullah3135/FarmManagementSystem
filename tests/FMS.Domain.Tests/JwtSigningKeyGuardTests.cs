using FMS.API.Security;

namespace FMS.Domain.Tests;

/// <summary>
/// The API signs its own access and refresh tokens, so the signing key IS the
/// authentication boundary: a token signed with a key an attacker knows is not a
/// weak credential, it is no credential. These pin the guard's rules, and the
/// last test keeps the guard wired into startup at all — deleting that one line
/// would otherwise leave every test in this suite passing.
/// </summary>
public class JwtSigningKeyGuardTests
{
    // ── missing ───────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingKey_IsRejected(string? key)
    {
        Assert.NotNull(JwtSigningKeyGuard.Validate(key, isDevelopment: false));
        Assert.NotNull(JwtSigningKeyGuard.Validate(key, isDevelopment: true));
    }

    // ── the committed development key ─────────────────────

    [Fact]
    public void Validate_CommittedDevelopmentKey_IsAllowedInDevelopmentOnly()
    {
        Assert.Null(JwtSigningKeyGuard.Validate(JwtSigningKeyGuard.DevelopmentPlaceholderKey, isDevelopment: true));

        var productionProblem = JwtSigningKeyGuard.Validate(
            JwtSigningKeyGuard.DevelopmentPlaceholderKey, isDevelopment: false);

        Assert.NotNull(productionProblem);
        Assert.Contains("placeholder", productionProblem);
        // The consequence, not just the rule: a reader of this repository could
        // forge any role, including the one that administers the system.
        Assert.Contains("SystemOwner", productionProblem);
    }

    // ── length, in bytes ──────────────────────────────────

    [Fact]
    public void Validate_KeyBelowTheMinimum_IsRejectedWithTheActualLength()
    {
        var problem = JwtSigningKeyGuard.Validate(new string('a', 31), isDevelopment: false);

        Assert.NotNull(problem);
        Assert.Contains("32 bytes", problem);
        Assert.Contains("31 bytes", problem);
    }

    [Fact]
    public void Validate_KeyAtTheMinimum_IsAccepted()
    {
        Assert.Null(JwtSigningKeyGuard.Validate(new string('a', 32), isDevelopment: false));
    }

    [Fact]
    public void Validate_LengthIsCountedInBytes_NotCharacters()
    {
        // 20 characters, 40 bytes - long enough for HS256.
        Assert.Null(JwtSigningKeyGuard.Validate(new string('é', 20), isDevelopment: false));
        // 15 characters, 30 bytes - rejected, because a short key is a short key
        // whatever it looks like.
        Assert.NotNull(JwtSigningKeyGuard.Validate(new string('é', 15), isDevelopment: false));
    }

    // ── the message an operator actually sees ─────────────

    [Fact]
    public void EnsureUsable_NamesTheEnvironmentAndTheFix()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            JwtSigningKeyGuard.EnsureUsable(null, "Production", isDevelopment: false));

        Assert.Contains("Production", error.Message);
        Assert.Contains("Jwt__SecretKey", error.Message);
        Assert.Contains("openssl rand -base64 48", error.Message);
        // Rotating is not free, so the operator is told what it costs.
        Assert.Contains("refresh token", error.Message);
    }

    [Fact]
    public void EnsureUsable_StrongKeyInAnyEnvironment_DoesNotThrow()
    {
        JwtSigningKeyGuard.EnsureUsable("a-properly-random-key-of-sufficient-length", "Production", false);
        JwtSigningKeyGuard.EnsureUsable(JwtSigningKeyGuard.DevelopmentPlaceholderKey, "Development", true);
    }

    // ── the guard must stay wired in ──────────────────────

    [Fact]
    public void Program_ChecksTheSigningKeyBeforeUsingIt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "FMS.API", "Program.cs"));

        var guardAt = source.IndexOf("JwtSigningKeyGuard.EnsureUsable", StringComparison.Ordinal);
        Assert.True(guardAt >= 0, "Program.cs no longer validates Jwt:SecretKey at startup.");

        // Reading the key and registering the DbContext must both come after it,
        // so the operator sees the guard's message rather than a downstream error.
        var keyUsedAt = source.IndexOf("Jwt:SecretKey", StringComparison.Ordinal);
        Assert.True(keyUsedAt > guardAt, "Program.cs reads Jwt:SecretKey before the guard runs.");

        var dbContextAt = source.IndexOf("AddDbContext<FmsDbContext>", StringComparison.Ordinal);
        Assert.True(dbContextAt > guardAt, "The signing-key guard must run before service registration.");
    }
}
