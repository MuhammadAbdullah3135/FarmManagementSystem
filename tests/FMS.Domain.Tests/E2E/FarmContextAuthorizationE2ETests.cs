using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Security: dual farm-context authorization.
/// These tests run the REAL FarmContextMiddleware (not the permissive inline stub)
/// and prove that a route {farmId} different from the header-validated farm is
/// rejected, and that headerless farm-scoped requests are membership-checked.
/// </summary>
public class FarmContextAuthorizationE2ETests : IClassFixture<FarmContextAuthorizationE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>Farm the seeded test user belongs to (the seed's own farm).</summary>
        public Guid MemberFarmId { get; private set; }

        /// <summary>Second farm the test user also belongs to.</summary>
        public Guid OtherMemberFarmId { get; private set; }

        /// <summary>Farm the test user has NO membership in.</summary>
        public Guid ForeignFarmId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            MemberFarmId = seed.FarmId;

            // Farm B: same account, user IS a member.
            var farmB = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Second Farm" };
            db.Farms.Add(farmB);
            db.UserFarms.Add(new UserFarm { UserId = JwtTokenHelper.TestUserId, FarmId = farmB.Id, Role = "FarmManager" });

            // Minimal configuration for Farm B so dashboard/animal calls resolve
            // (the summary projection navigates AnimalType/AnimalStatus, so both are required).
            var typeB = new AnimalType { Id = Guid.NewGuid(), FarmId = farmB.Id, Name = "Cattle" };
            var breedB = new Breed { Id = Guid.NewGuid(), Name = "Angus", AnimalType = typeB, AverageGestationDays = 283 };
            var statusB = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farmB.Id, Name = "Active", Category = FMS.Domain.Enums.AnimalStatusCategory.Active, IsSystemDefined = true };
            db.AnimalTypes.Add(typeB);
            db.Breeds.Add(breedB);
            db.AnimalStatuses.Add(statusB);
            db.Animals.Add(new Animal
            {
                Id = Guid.NewGuid(), FarmId = farmB.Id,
                TagNumber = "FARM-B-001", Name = "Farm B Cow",
                AnimalType = typeB, Breed = breedB,
                AnimalStatus = statusB,
                DateOfBirth = DateTime.UtcNow.AddYears(-2)
            });

            // Farm C: same account, user is NOT a member.
            var farmC = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Foreign Farm" };
            db.Farms.Add(farmC);
            var typeC = new AnimalType { Id = Guid.NewGuid(), FarmId = farmC.Id, Name = "Cattle" };
            var statusC = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farmC.Id, Name = "Active", Category = FMS.Domain.Enums.AnimalStatusCategory.Active, IsSystemDefined = true };
            db.AnimalTypes.Add(typeC);
            db.AnimalStatuses.Add(statusC);
            db.Animals.Add(new Animal
            {
                Id = Guid.NewGuid(), FarmId = farmC.Id,
                TagNumber = "FARM-C-001", Name = "Farm C Cow",
                AnimalType = typeC, AnimalStatus = statusC,
                DateOfBirth = DateTime.UtcNow.AddYears(-2)
            });

            await db.SaveChangesAsync();
            OtherMemberFarmId = farmB.Id;
            ForeignFarmId = farmC.Id;
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public FarmContextAuthorizationE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    private class SummaryDto
    {
        public int TotalAnimals { get; set; }
    }

    /// <summary>Authenticated client with an optional X-Farm-Id header.</summary>
    private HttpClient ClientWithHeader(Guid? headerFarm)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(JwtTokenHelper.TestUserId, new[] { "FarmManager" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerFarm.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());
        return client;
    }

    // ── 1. The core bug: valid header for member farm A, route points at farm B ──

    [Fact]
    public async Task MismatchedRouteFarm_Read_IsRejected()
    {
        var client = ClientWithHeader(_factory.MemberFarmId);
        var response = await client.GetAsync($"/api/farm/{_factory.OtherMemberFarmId}/dashboard/summary");
        _output.WriteLine($"read mismatch -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 2. Same gap on a write path ──────────────────────────────────────────────

    [Fact]
    public async Task MismatchedRouteFarm_Write_IsRejected()
    {
        var client = ClientWithHeader(_factory.MemberFarmId);
        var body = new
        {
            TagNumber = $"HACK-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Injected Cow",
            DateOfBirth = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        var response = await client.PostAsJsonAsync($"/api/farm/{_factory.OtherMemberFarmId}/animals", body);
        _output.WriteLine($"write mismatch -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 3. Even switching to a farm the user legitimately belongs to is rejected:
    //        the header defines the session's farm for the whole request. ────────

    [Fact]
    public async Task RouteFarm_DifferentFromHeader_EvenIfMember_IsRejected()
    {
        var client = ClientWithHeader(_factory.MemberFarmId);
        var response = await client.GetAsync($"/api/farm/{_factory.OtherMemberFarmId}/animals");
        _output.WriteLine($"member-route switch -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 4. The sibling bypass: no header at all, route points at a non-member farm ──

    [Fact]
    public async Task Headerless_RouteForeignFarm_IsRejected()
    {
        var client = ClientWithHeader(null);
        var response = await client.GetAsync($"/api/farm/{_factory.ForeignFarmId}/animals");
        _output.WriteLine($"headerless foreign -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 5. New legitimate path: no header, route farm the user belongs to ────────

    [Fact]
    public async Task Headerless_RouteMemberFarm_IsAllowed()
    {
        var client = ClientWithHeader(null);
        var response = await client.GetAsync($"/api/farm/{_factory.OtherMemberFarmId}/dashboard/summary");
        _output.WriteLine($"headerless member -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Prove the data really came from the route farm (Farm B has exactly 1 animal).
        var summary = await response.Content.ReadFromJsonAsync<SummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.TotalAnimals);
    }

    // ── 6. Regression control: matching header and route still works ─────────────

    [Fact]
    public async Task MatchingHeaderAndRoute_IsAllowed()
    {
        var client = ClientWithHeader(_factory.MemberFarmId);
        var response = await client.GetAsync($"/api/farm/{_factory.MemberFarmId}/dashboard/summary");
        _output.WriteLine($"matching pair -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
