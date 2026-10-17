using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Enums;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using FMS.API.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FMS.Domain.Tests.E2E;
using Xunit;

namespace FMS.Domain.Tests.Demo;

/// <summary>
/// End-to-end coverage for the shared demo account over a real in-process HTTP pipeline
/// (TestServer) against an InMemory database. Runs the same controllers, <c>AuthService</c>,
/// <c>DemoDataSeeder</c> and <c>FarmDefaultsSeeder</c> the API wires up, so it exercises the
/// lazy-seed, response-shape and reset paths a browser actually hits.
/// </summary>
public class DemoSmokeTests
{
    private static IHost BuildHost(bool demoEnabled)
    {
        var dbName = $"DemoSmoke_{Guid.NewGuid()}";
        var builder = new HostBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frontend:BaseUrl"] = "http://localhost:3000",
                    ["Jwt:RefreshTokenExpiryDays"] = "30",
                    ["Jwt:SecretKey"] = "TestSecretKeyThatIsAtLeast32BytesLong!!",
                    ["Jwt:Issuer"] = "TestIssuer",
                    ["Jwt:Audience"] = "TestAudience",
                    ["Jwt:AccessTokenExpiryMinutes"] = "60",
                    ["Demo:Enabled"] = demoEnabled ? "true" : "false",
                    ["Demo:DemoEmail"] = "demo@farm.local",
                });
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    // Registered from the same helper Program.cs uses, so these tests exercise
                    // the real demo policies rather than a drifting copy.
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddDbContext<FmsDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddControllers()
                        .AddApplicationPart(typeof(FMS.API.Controllers.AuthController).Assembly)
                        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
                            new System.Text.Json.Serialization.JsonStringEnumConverter()));
                    services.AddScoped<IFarmContextService, FarmContext>();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUserService, CurrentUserService>();
                    services.AddScoped<IAuthService, AuthService>();
                    services.AddScoped<IJwtTokenService, JwtTokenService>();
                    services.AddRateLimiter(DemoRateLimitPolicies.Add);
                    services.AddSingleton<IEmailService, PasswordResetE2ETests.TestEmailService>();
                    services.AddLogging();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization();
                });
            });
        var host = builder.Start();
        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<FmsDbContext>().Database.EnsureCreated();
        }
        return host;
    }

    private static async Task<(int animals, int tasks, string? farmName, bool isDemo, Guid farmId)> SnapshotAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var demoUser = await db.Users.FirstOrDefaultAsync(u => u.IsDemo);
        if (demoUser == null) return (0, 0, null, false, Guid.Empty);
        var farmId = await db.UserFarms.Where(uf => uf.UserId == demoUser.Id).Select(uf => uf.FarmId).FirstOrDefaultAsync();
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId);
        var animals = await db.Animals.CountAsync(a => a.FarmId == farmId);
        var tasks = await db.FarmTasks.CountAsync(t => t.FarmId == farmId);
        return (animals, tasks, farm?.Name, demoUser.IsDemo, farmId);
    }

    [Fact]
    public async Task Demo_login_seeds_the_farm_and_reset_restores_seed_state()
    {
        using var host = BuildHost(demoEnabled: true);
        var client = host.GetTestClient();

        // Nothing seeded before the first demo login (lazy creation).
        Assert.Equal(0, (await SnapshotAsync(host)).animals);

        var login = await client.PostAsJsonAsync("/api/auth/demo-login", new { });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var token = root.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal("demo@farm.local", root.GetProperty("email").GetString());
        // The flag the client's DemoBanner reads.
        var hasIsDemo = root.TryGetProperty("isDemo", out var isDemoProp) && isDemoProp.ValueKind == JsonValueKind.True;
        var roles = root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();

        var seeded = await SnapshotAsync(host);
        Assert.Equal("Sunrise Ranch", seeded.farmName);
        Assert.True(seeded.isDemo);
        Assert.Equal(9, seeded.animals);
        Assert.Equal(4, seeded.tasks);

        // Make the demo messy.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            db.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
            {
                Id = Guid.NewGuid(), FarmId = seeded.farmId, Title = "visitor-created task",
                Priority = FarmTaskPriority.Low, Status = FarmTaskStatus.Pending,
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(5, (await SnapshotAsync(host)).tasks);

        // Reset (the demo user is SystemOwner). Use the token demo-login actually returned —
        // the client has no other token — so this proves the fresh demo session can reset.
        var resetClient = host.GetTestClient();
        resetClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var reset = await resetClient.PostAsync("/api/auth/demo-reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(4, (await SnapshotAsync(host)).tasks);

        Assert.True(hasIsDemo, "demo-login response must carry isDemo=true for the client banner.");
        Assert.Contains("SystemOwner", roles);
    }

    [Fact]
    public async Task Demo_endpoints_are_rejected_when_disabled()
    {
        using var host = BuildHost(demoEnabled: false);
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync("/api/auth/demo-login", new { });
        Assert.Equal(HttpStatusCode.Gone, login.StatusCode);

        var resetClient = host.GetTestClient();
        resetClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", JwtTokenHelper.GenerateTestToken(Guid.NewGuid(), new[] { "SystemOwner" }));
        var reset = await resetClient.PostAsync("/api/auth/demo-reset", null);
        Assert.Equal(HttpStatusCode.Gone, reset.StatusCode);
    }

    [Fact]
    public async Task Demo_login_is_rate_limited_over_the_policy_budget()
    {
        using var host = BuildHost(demoEnabled: true);
        var client = host.GetTestClient();

        // The "demo-login" policy permits 10 per minute per IP. The first ten are served; the
        // eleventh is refused by the limiter (not by the handler).
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/demo-login", new { })).StatusCode);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(10));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Fact]
    public async Task Demo_reset_is_rate_limited_over_the_policy_budget()
    {
        using var host = BuildHost(demoEnabled: true);

        // Seed the demo user and get a SystemOwner token through the real login path.
        var login = await host.GetTestClient().PostAsJsonAsync("/api/auth/demo-login", new { });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();

        var resetClient = host.GetTestClient();
        resetClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // The "demo-reset" policy permits 5 per minute per IP; the sixth is refused.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await resetClient.PostAsync("/api/auth/demo-reset", null)).StatusCode);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(5));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }
}
