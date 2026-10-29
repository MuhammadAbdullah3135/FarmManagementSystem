using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Entities;
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

    private sealed record DemoSnapshot(
        int Animals, int Tasks, string? FarmName, bool IsDemo, Guid FarmId,
        IReadOnlyDictionary<string, int> Counts);

    private static async Task<DemoSnapshot> SnapshotAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var demoUser = await db.Users.FirstOrDefaultAsync(u => u.IsDemo);
        if (demoUser == null) return new DemoSnapshot(0, 0, null, false, Guid.Empty,
            new Dictionary<string, int>());
        var farmId = await db.UserFarms.Where(uf => uf.UserId == demoUser.Id)
            .Select(uf => uf.FarmId).FirstOrDefaultAsync();
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Id == farmId);
        var counts = new Dictionary<string, int>
        {
            ["animals"] = await db.Animals.CountAsync(a => a.FarmId == farmId),
            ["farmTasks"] = await db.FarmTasks.CountAsync(t => t.FarmId == farmId),
            ["locations"] = await db.Locations.CountAsync(l => l.FarmId == farmId),
            ["weights"] = await db.WeightRecords.CountAsync(w => w.FarmId == farmId),
            ["breeding"] = await db.BreedingRecords.CountAsync(b => b.FarmId == farmId),
            ["gestation"] = await db.GestationRecords.CountAsync(g => g.FarmId == farmId),
            ["gestationChecks"] = await db.GestationHealthChecks.CountAsync(g => g.FarmId == farmId),
            ["births"] = await db.BirthRecords.CountAsync(b => b.FarmId == farmId),
            ["offspring"] = await db.BirthOffspring.CountAsync(b => b.FarmId == farmId),
            ["medicines"] = await db.Medicines.CountAsync(m => m.FarmId == farmId),
            ["medicineStocks"] = await db.MedicineStocks.CountAsync(m => m.FarmId == farmId),
            ["vaccineTypes"] = await db.VaccineTypes.CountAsync(v => v.FarmId == farmId),
            ["medicalRecords"] = await db.MedicalRecords.CountAsync(m => m.FarmId == farmId),
            ["vaccinations"] = await db.VaccinationRecords.CountAsync(v => v.FarmId == farmId),
            ["vaccinationSchedules"] = await db.VaccinationSchedules.CountAsync(v => v.FarmId == farmId),
            ["weightSchedules"] = await db.WeightCheckSchedules.CountAsync(w => w.FarmId == farmId),
            ["feedTypes"] = await db.FeedTypes.CountAsync(f => f.FarmId == farmId),
            ["feedMovements"] = await db.FeedStockMovements.CountAsync(f => f.FarmId == farmId),
            ["feedRecords"] = await db.FeedRecords.CountAsync(f => f.FarmId == farmId),
            ["dietPlans"] = await db.DietPlans.CountAsync(d => d.FarmId == farmId),
            ["dietPlanItems"] = await db.DietPlanItems.CountAsync(d =>
                db.DietPlans.Any(p => p.Id == d.DietPlanId && p.FarmId == farmId)),
            ["feedingSchedules"] = await db.FeedingSchedules.CountAsync(f => f.FarmId == farmId),
            ["feedingTasks"] = await db.FeedingTasks.CountAsync(f => f.FarmId == farmId),
            ["inventoryItems"] = await db.InventoryItems.CountAsync(i => i.FarmId == farmId),
            ["stockMovements"] = await db.StockMovements.CountAsync(s => s.FarmId == farmId),
            ["suppliers"] = await db.Suppliers.CountAsync(s => s.FarmId == farmId),
            ["customers"] = await db.Customers.CountAsync(c => c.FarmId == farmId),
            ["purchases"] = await db.SupplierPurchases.CountAsync(p => p.FarmId == farmId),
            ["sales"] = await db.CustomerSales.CountAsync(s => s.FarmId == farmId),
            ["expenses"] = await db.Expenses.CountAsync(e => e.FarmId == farmId),
            ["income"] = await db.IncomeRecords.CountAsync(i => i.FarmId == farmId),
            ["salaryPayments"] = await db.SalaryPayments.CountAsync(s => s.FarmId == farmId),
            ["employees"] = await db.Employees.CountAsync(e => e.FarmId == farmId),
            ["attendance"] = await db.AttendanceRecords.CountAsync(a => a.FarmId == farmId),
            ["reviews"] = await db.PerformanceReviews.CountAsync(r => r.FarmId == farmId),
        };
        return new DemoSnapshot(counts["animals"], counts["farmTasks"], farm?.Name,
            demoUser.IsDemo, farmId, counts);
    }

    [Fact]
    public async Task Demo_login_seeds_the_farm_and_reset_restores_seed_state()
    {
        using var host = BuildHost(demoEnabled: true);
        var client = host.GetTestClient();

        // Nothing seeded before the first demo login (lazy creation).
        Assert.Equal(0, (await SnapshotAsync(host)).Animals);

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
        Assert.Equal("Sunrise Ranch", seeded.FarmName);
        Assert.True(seeded.IsDemo);
        Assert.Equal(30, seeded.Counts["animals"]);
        Assert.Equal(18, seeded.Counts["farmTasks"]);
        Assert.Equal(6, seeded.Counts["locations"]);
        Assert.Equal(60, seeded.Counts["weights"]);
        Assert.Equal(10, seeded.Counts["breeding"]);
        Assert.Equal(4, seeded.Counts["gestation"]);
        Assert.Equal(8, seeded.Counts["gestationChecks"]);
        Assert.Equal(4, seeded.Counts["births"]);
        Assert.Equal(8, seeded.Counts["offspring"]);
        Assert.Equal(6, seeded.Counts["medicines"]);
        Assert.Equal(10, seeded.Counts["medicineStocks"]);
        Assert.Equal(4, seeded.Counts["vaccineTypes"]);
        Assert.Equal(14, seeded.Counts["medicalRecords"]);
        Assert.Equal(18, seeded.Counts["vaccinations"]);
        Assert.Equal(5, seeded.Counts["vaccinationSchedules"]);
        Assert.Equal(3, seeded.Counts["weightSchedules"]);
        Assert.Equal(8, seeded.Counts["feedTypes"]);
        Assert.Equal(20, seeded.Counts["feedMovements"]);
        Assert.Equal(30, seeded.Counts["feedRecords"]);
        Assert.Equal(5, seeded.Counts["dietPlans"]);
        Assert.Equal(15, seeded.Counts["dietPlanItems"]);
        Assert.Equal(6, seeded.Counts["feedingSchedules"]);
        Assert.Equal(20, seeded.Counts["feedingTasks"]);
        Assert.Equal(12, seeded.Counts["inventoryItems"]);
        Assert.Equal(24, seeded.Counts["stockMovements"]);
        Assert.Equal(4, seeded.Counts["suppliers"]);
        Assert.Equal(3, seeded.Counts["customers"]);
        Assert.Equal(6, seeded.Counts["purchases"]);
        Assert.Equal(6, seeded.Counts["sales"]);
        Assert.Equal(30, seeded.Counts["expenses"]);
        Assert.Equal(18, seeded.Counts["income"]);
        Assert.Equal(9, seeded.Counts["salaryPayments"]);
        Assert.Equal(3, seeded.Counts["employees"]);
        Assert.Equal(30, seeded.Counts["attendance"]);
        Assert.Equal(3, seeded.Counts["reviews"]);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            var animalTypeCounts = await db.Animals.Where(a => a.FarmId == seeded.FarmId)
                .GroupBy(a => a.AnimalType.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count);
            Assert.Equal(12, animalTypeCounts["Cattle"]);
            Assert.Equal(5, animalTypeCounts["Buffalo"]);
            Assert.Equal(5, animalTypeCounts["Goat"]);
            Assert.Equal(4, animalTypeCounts["Sheep"]);
            Assert.Equal(4, animalTypeCounts["Poultry"]);

            var statusCounts = await db.Animals.Where(a => a.FarmId == seeded.FarmId)
                .GroupBy(a => a.AnimalStatus.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count);
            Assert.Equal(9, statusCounts["Active"]);
            Assert.Equal(6, statusCounts["Lactating"]);
            Assert.Equal(4, statusCounts["Pregnant"]);
            Assert.Equal(3, statusCounts["Dry"]);
            Assert.Equal(3, statusCounts["Sick"]);
            Assert.Equal(3, statusCounts["Sold"]);
            Assert.Equal(2, statusCounts["Deceased"]);

            var deceasedAnimalIds = await db.Animals
                .Where(a => a.FarmId == seeded.FarmId && a.AnimalStatus.Name == "Deceased")
                .Select(a => a.Id).ToListAsync();
            foreach (var animalId in deceasedAnimalIds)
            {
                Assert.True(await db.MedicalRecords.AnyAsync(m =>
                        m.FarmId == seeded.FarmId && m.AnimalId == animalId &&
                        m.Status == MedicalRecordStatus.Resolved),
                    $"Deceased animal {animalId} must have a resolved medical record.");
            }
        }

        // Make the demo messy.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            db.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
            {
                Id = Guid.NewGuid(), FarmId = seeded.FarmId, Title = "visitor-created task",
                Priority = FarmTaskPriority.Low, Status = FarmTaskStatus.Pending,
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(19, (await SnapshotAsync(host)).Counts["farmTasks"]);

        // Reset (the demo user is SystemOwner). Use the token demo-login actually returned —
        // the client has no other token — so this proves the fresh demo session can reset.
        var resetClient = host.GetTestClient();
        resetClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var reset = await resetClient.PostAsync("/api/auth/demo-reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var restored = await SnapshotAsync(host);
        foreach (var key in seeded.Counts.Keys)
            Assert.Equal(seeded.Counts[key], restored.Counts[key]);

        Assert.True(hasIsDemo, "demo-login response must carry isDemo=true for the client banner.");
        Assert.Contains("SystemOwner", roles);
    }

    [Fact]
    public async Task Demo_reset_leaves_non_demo_farms_untouched()
    {
        using var host = BuildHost(demoEnabled: true);
        var login = await host.GetTestClient().PostAsJsonAsync("/api/auth/demo-login", new { });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();

        Guid nonDemoFarmId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            var account = new Account { Id = Guid.NewGuid(), Name = "Non-demo account" };
            var user = new User
            {
                Id = Guid.NewGuid(), AccountId = account.Id,
                Email = "owner@non-demo.example", PasswordHash = "not-used",
                FirstName = "Non", LastName = "Demo",
            };
            var farm = new Farm
            {
                Id = Guid.NewGuid(), AccountId = account.Id, Name = "Non-demo farm",
            };
            nonDemoFarmId = farm.Id;
            db.Accounts.Add(account);
            db.Users.Add(user);
            db.Farms.Add(farm);
            db.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
            {
                Id = Guid.NewGuid(), FarmId = farm.Id, Title = "non-demo task",
                Priority = FarmTaskPriority.High, Status = FarmTaskStatus.Pending,
            });
            await db.SaveChangesAsync();
        }

        var resetClient = host.GetTestClient();
        resetClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var reset = await resetClient.PostAsync("/api/auth/demo-reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            Assert.Equal(1, await db.FarmTasks.CountAsync(t => t.FarmId == nonDemoFarmId));
            Assert.True(await db.Farms.AnyAsync(f => f.Id == nonDemoFarmId));
            Assert.True(await db.Users.AnyAsync(u => u.Email == "owner@non-demo.example"));
        }
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
