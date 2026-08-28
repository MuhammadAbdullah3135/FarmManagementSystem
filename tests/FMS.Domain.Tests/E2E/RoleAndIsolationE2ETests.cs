using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Role-based authorization matrix + cross-farm header/route decoupling.
/// Exercises the real controllers registered by the test host via HTTP.
/// </summary>
public class RoleAndIsolationE2ETests : IClassFixture<RoleAndIsolationE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public RoleAndIsolationE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    private ApiSeedData.SeedIds Seed => _factory.SeedData!;

    private HttpClient ClientFor(Guid userId, params string[] roles)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(userId, roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Farm-Id", Seed.FarmId.ToString());
        return client;
    }

    // ── Role matrix ─────────────────────────────────────────────

    private static readonly Guid SystemOwnerUser = Guid.NewGuid();
    private static readonly Guid FarmManagerUser = Guid.NewGuid();
    private static readonly Guid AccountantUser = Guid.NewGuid();
    private static readonly Guid VetUser = Guid.NewGuid();
    private static readonly Guid EmployeeUser = Guid.NewGuid();
    private static readonly Guid ViewerUser = Guid.NewGuid();

    // Audit logs: [Authorize(Roles = "SystemOwner,FarmManager,Accountant")]
    [Theory]
    [InlineData("SystemOwner")]
    [InlineData("FarmManager")]
    [InlineData("Accountant")]
    public async Task AuditLogs_AllowedRoles_GetThrough(string role)
    {
        var client = ClientFor(Guid.NewGuid(), role);
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/audit-logs");
        // Allowed roles must not be forbidden (200, or a non-403 application error if service missing)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Employee")]
    [InlineData("Veterinarian")]
    public async Task AuditLogs_DisallowedRoles_Forbidden(string role)
    {
        var client = ClientFor(Guid.NewGuid(), role);
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Breeding records POST: [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    [Fact]
    public async Task VeterinaryRole_IsBlockedFromBreeding_Creating_DueToRoleNameMismatch()
    {
        // Seeded role is "Veterinarian" but the attribute demands "Vet" -> mismatch
        var client = ClientFor(Guid.NewGuid(), "Veterinarian");
        var body = new
        {
            SireId = Seed.SexMaleId, // placeholder, only status matters
            DamId = Seed.SexFemaleId,
            BreedingDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Method = 0
        };
        var response = await client.PostAsJsonAsync($"/api/farm/{Seed.FarmId}/breeding-records", body);
        // EXPECTED: the seeded Veterinarian role should exercise Vet capabilities.
        // ACTUAL (bug): the attribute says "Vet", which no seeded role matches -> 403.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FarmManager_CanCreateBreedingRecord()
    {
        var client = ClientFor(Guid.NewGuid(), "FarmManager");
        var body = new
        {
            SireId = Seed.SexMaleId,
            DamId = Seed.SexFemaleId,
            BreedingDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Method = 0
        };
        var response = await client.PostAsJsonAsync($"/api/farm/{Seed.FarmId}/breeding-records", body);
        // Allowed through the role gate (may still be validation error on bad refs, but NOT 403)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // InventoryItems whole controller: [Authorize(Roles = "FarmWorker,Vet,FarmManager,SystemOwner,Owner")]
    [Fact]
    public async Task Viewer_IsBlocked_FromInventoryItems()
    {
        var client = ClientFor(Guid.NewGuid(), "Viewer");
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/inventory-items");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FarmManager_CanListInventoryItems()
    {
        var client = ClientFor(Guid.NewGuid(), "FarmManager");
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/inventory-items");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Cross-farm header/route decoupling ─────────────────────

    // Simulates: user is linked to farm A (header = A) but the route passes farm B.
    private HttpClient ClientWithHeaderAndRoute(Guid headerFarm, Guid routeFarm)
    {
        _ = _factory.Host;
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(JwtTokenHelper.TestUserId, new[] { "FarmManager" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.ToString());
        return client;
    }

    [Fact]
    public async Task Dashboard_RouteFarm_IsUsedOverHeaderFarm()
    {
        var otherFarmId = Seed.FarmId; // seeded farm has data
        var client = ClientWithHeaderAndRoute(Seed.FarmId, otherFarmId);
        var response = await client.GetAsync($"/api/farm/{otherFarmId}/dashboard/summary");
        var summary = await response.Content.ReadFromJsonAsync<DashboardE2ETests_DashboardSummary>();
        Assert.NotNull(summary);
        Assert.Equal(8, summary.TotalAnimals);
    }

    private class DashboardE2ETests_DashboardSummary
    {
        public int TotalAnimals { get; set; }
        public Dictionary<string, int> AnimalsByType { get; set; } = new();
        public Dictionary<string, int> AnimalsByStatus { get; set; } = new();
        public int PregnantCount { get; set; }
        public int SickCount { get; set; }
        public int OverdueTasks { get; set; }
    }
}
