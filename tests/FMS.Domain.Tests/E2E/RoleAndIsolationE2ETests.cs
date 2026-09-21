using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Farm-role authorization matrix.
///
/// Runs the REAL FarmContextMiddleware (not the permissive inline stub), so the
/// role a request is authorized with is the caller's farm membership role
/// (<c>UserFarm.Role</c>) for the resolved farm — not the account role baked into
/// the JWT.
///
/// Every token here deliberately carries the HIGHEST account role
/// (<c>SystemOwner</c>) while the seeded farm roles differ. That is what makes a
/// 403 meaningful: only farm-role enforcement can produce it, because the account
/// role alone would allow every request.
/// </summary>
public class RoleAndIsolationE2ETests : IClassFixture<RoleAndIsolationE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            // One member per farm role on the seeded farm. The account role is not
            // stored here at all — it only ever exists in the test JWT.
            AddMember(db, seed, SystemOwnerUser, "SystemOwner");
            AddMember(db, seed, FarmManagerUser, "FarmManager");
            AddMember(db, seed, AccountantUser, "Accountant");
            AddMember(db, seed, VetUser, "Veterinarian");
            AddMember(db, seed, EmployeeUser, "Employee");
            AddMember(db, seed, ViewerUser, "Viewer");
            await db.SaveChangesAsync();
        }

        private static void AddMember(FmsDbContext db, ApiSeedData.SeedIds seed, Guid userId, string farmRole)
        {
            db.Users.Add(new User
            {
                Id = userId,
                AccountId = seed.AccountId,
                Email = $"{farmRole.ToLowerInvariant()}@role-matrix.test",
                PasswordHash = "not-used-tests-authenticate-with-generated-tokens",
                FirstName = farmRole,
                LastName = "Member",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });

            db.UserFarms.Add(new UserFarm
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FarmId = seed.FarmId,
                Role = farmRole,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public RoleAndIsolationE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    private ApiSeedData.SeedIds Seed => _factory.SeedData!;

    /// <summary>
    /// The account role every token carries. Higher than most farm roles, so a
    /// farm-scoped denial cannot be explained by the account role.
    /// </summary>
    private const string AccountRole = "SystemOwner";

    // Fixed identities, one per farm role, seeded by the factory above.
    private static readonly Guid SystemOwnerUser = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid FarmManagerUser = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid AccountantUser = new("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid VetUser = new("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid EmployeeUser = new("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid ViewerUser = new("aaaaaaaa-0000-0000-0000-000000000006");

    private static Guid UserForRole(string farmRole) => farmRole switch
    {
        "SystemOwner" => SystemOwnerUser,
        "FarmManager" => FarmManagerUser,
        "Accountant" => AccountantUser,
        "Veterinarian" => VetUser,
        "Employee" => EmployeeUser,
        "Viewer" => ViewerUser,
        _ => throw new ArgumentOutOfRangeException(nameof(farmRole), farmRole, "Unknown farm role"),
    };

    private HttpClient ClientFor(Guid userId)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(userId, new[] { AccountRole });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Farm-Id", Seed.FarmId.ToString());
        return client;
    }

    // ── Audit logs: [Authorize(Roles = "SystemOwner,FarmManager,Accountant")] ──

    [Theory]
    [InlineData("SystemOwner")]
    [InlineData("FarmManager")]
    [InlineData("Accountant")]
    public async Task AuditLogs_AllowedFarmRoles_AreNotForbidden(string farmRole)
    {
        var client = ClientFor(UserForRole(farmRole));
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/audit-logs");
        _output.WriteLine($"audit-logs as farm {farmRole} -> {response.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Veterinarian")]
    [InlineData("Employee")]
    [InlineData("Viewer")]
    public async Task AuditLogs_DisallowedFarmRoles_AreForbidden_DespiteSystemOwnerAccountRole(string farmRole)
    {
        var client = ClientFor(UserForRole(farmRole));
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/audit-logs");
        _output.WriteLine($"audit-logs as farm {farmRole} -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Inventory items: [Authorize(Roles = "Employee,Veterinarian,FarmManager,SystemOwner")] ──

    [Theory]
    [InlineData("Employee")]
    [InlineData("Veterinarian")]
    [InlineData("FarmManager")]
    [InlineData("SystemOwner")]
    public async Task InventoryItems_AllowedFarmRoles_AreNotForbidden(string farmRole)
    {
        var client = ClientFor(UserForRole(farmRole));
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/inventory-items");
        _output.WriteLine($"inventory-items as farm {farmRole} -> {response.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InventoryItems_ViewerFarmRole_IsForbidden_DespiteSystemOwnerAccountRole()
    {
        var client = ClientFor(ViewerUser);
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/inventory-items");
        _output.WriteLine($"inventory-items as farm Viewer -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Breeding writes: [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")] ──

    [Fact]
    public async Task BreedingWrites_VeterinarianFarmRole_IsNotForbidden()
    {
        var client = ClientFor(VetUser);
        var response = await client.PostAsJsonAsync($"/api/farm/{Seed.FarmId}/breeding-records", BreedingBody());
        _output.WriteLine($"breeding POST as farm Veterinarian -> {response.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BreedingWrites_AccountantFarmRole_IsForbidden_DespiteSystemOwnerAccountRole()
    {
        var client = ClientFor(AccountantUser);
        var response = await client.PostAsJsonAsync($"/api/farm/{Seed.FarmId}/breeding-records", BreedingBody());
        _output.WriteLine($"breeding POST as farm Accountant -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private object BreedingBody() => new
    {
        SireId = Seed.SexMaleId, // placeholder refs; only the status code matters
        DamId = Seed.SexFemaleId,
        BreedingDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        Method = 0,
    };

    // ── Account-scoped routes must be unaffected by the farm role ─────────────

    private sealed class AuthMeDto
    {
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string? AccountId { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    private sealed class FarmListDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UserFarmRole { get; set; }
    }

    [Fact]
    public async Task AuthMe_ReturnsTheAccountRole_NotTheFarmRole()
    {
        // Account role is SystemOwner; the farm role is Veterinarian. /api/auth is
        // account-scoped and must keep reporting the account role.
        var client = ClientFor(VetUser);
        var me = await client.GetFromJsonAsync<AuthMeDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Contains(AccountRole, me!.Roles);
        Assert.DoesNotContain("Veterinarian", me.Roles);
        _output.WriteLine($"auth/me roles -> {string.Join(",", me.Roles)}");
    }

    [Fact]
    public async Task FarmList_StillResolves_AndReportsTheFarmRole()
    {
        var client = ClientFor(VetUser);
        var farms = await client.GetFromJsonAsync<List<FarmListDto>>("/api/farms");

        var farm = Assert.Single(farms!, f => f.Id == Seed.FarmId);
        Assert.Equal("Veterinarian", farm.UserFarmRole);
        _output.WriteLine($"farm list role -> {farm.UserFarmRole}");
    }

    // ── Regression control: a matching header/route still reaches the farm ────

    [Fact]
    public async Task MatchingHeaderAndRoute_ReachesTheFarm()
    {
        var client = ClientFor(FarmManagerUser);
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(8, summary!.TotalAnimals);
    }

    private sealed class DashboardSummaryDto
    {
        public int TotalAnimals { get; set; }
    }
}
