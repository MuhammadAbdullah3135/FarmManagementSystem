namespace FMS.Domain.Tests.Sync;

/// <summary>
/// The request pipeline is configured in source, and its order is what makes farm scoping an
/// authorization boundary rather than a convenience: the farm context has to be resolved from the
/// authenticated principal <em>after</em> authentication and <em>before</em> authorization, so a
/// farm-scoped <c>[Authorize(Roles = …)]</c> is evaluated against the farm role.
///
/// <para>
/// Phase 5.3 adds the first new farm-scoped route since that pipeline was fixed, so this asserts
/// the wiring it relies on — the same shape as the other source tripwires in this suite
/// (<c>UploadStaticServingTests</c>, <c>JwtSigningKeyGuardTests</c>), because a behavioural test
/// cannot see the order itself.
/// </para>
/// </summary>
public class MiddlewareOrderTripwireTests
{
    private static string ProgramSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, "src", "FMS.API", "Program.cs");
        Assert.True(File.Exists(path), $"Expected to find Program.cs at {path}");

        return File.ReadAllText(path);
    }

    [Fact]
    public void FarmContext_IsResolvedBetweenAuthenticationAndAuthorization()
    {
        var source = ProgramSource();

        var authentication = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var farmContext = source.IndexOf("app.UseMiddleware<FarmContextMiddleware>();", StringComparison.Ordinal);
        var authorization = source.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.True(authentication >= 0, "Program.cs no longer calls UseAuthentication().");
        Assert.True(farmContext >= 0, "Program.cs no longer registers FarmContextMiddleware.");
        Assert.True(authorization >= 0, "Program.cs no longer calls UseAuthorization().");

        Assert.True(authentication < farmContext,
            "FarmContextMiddleware must run after authentication: it reads the authenticated user to resolve membership.");
        Assert.True(farmContext < authorization,
            "FarmContextMiddleware must run before authorization: farm-scoped role checks are evaluated against the farm role it resolves.");
    }

    [Fact]
    public void FarmContextMiddleware_StillEnforcesTheRouteFarmAgainstTheHeaderFarm()
    {
        // The sync endpoint's farm scoping is this middleware's, not its own: a queued mutation is
        // applied under the farm the route names, and the header cannot override it.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        var path = Path.Combine(directory!.FullName, "src", "FMS.API", "Middleware", "FarmContextMiddleware.cs");
        Assert.True(File.Exists(path), $"Expected to find FarmContextMiddleware.cs at {path}");

        var source = File.ReadAllText(path);

        Assert.Contains("RouteValues.TryGetValue(\"farmId\"", source);
        Assert.Contains("route farm does not match the header farm", source);
        Assert.Contains("not a member of the route farm", source);
    }
}
