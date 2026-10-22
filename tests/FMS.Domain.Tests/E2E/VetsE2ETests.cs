using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Vets are readable and writable by any member of the farm, matching the existing
/// clinical-record rule. The real FarmContextMiddleware runs, so the denials below
/// exercise production membership enforcement rather than a permissive stub.
/// </summary>
public class VetsE2ETests : IClassFixture<VetsE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        public Guid FarmId { get; private set; }
        public Guid ForeignFarmId { get; private set; }
        public Guid ForeignVetId { get; private set; }
        public Guid NonMemberUserId { get; private set; }
        public Guid AccountantUserId { get; private set; }

        protected override bool UsesRealFarmContextMiddleware => true;

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            FarmId = seed.FarmId;

            NonMemberUserId = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = NonMemberUserId,
                AccountId = seed.AccountId,
                Email = "nonmember@test.com",
                PasswordHash = "hashed",
                FirstName = "Non",
                LastName = "Member"
            });

            AccountantUserId = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = AccountantUserId,
                AccountId = seed.AccountId,
                Email = "accountant.vet@test.com",
                PasswordHash = "hashed",
                FirstName = "Account",
                LastName = "Ant"
            });
            db.UserFarms.Add(new UserFarm
            {
                UserId = AccountantUserId,
                FarmId = seed.FarmId,
                Role = "Accountant"
            });

            var foreignFarm = new Farm
            {
                Id = Guid.NewGuid(),
                AccountId = seed.AccountId,
                Name = "Foreign Farm"
            };
            var foreignVet = new Vet
            {
                Id = Guid.NewGuid(),
                FarmId = foreignFarm.Id,
                Name = "Dr. Foreign",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Farms.Add(foreignFarm);
            db.Vets.Add(foreignVet);

            await db.SaveChangesAsync();
            ForeignFarmId = foreignFarm.Id;
            ForeignVetId = foreignVet.Id;
        }
    }

    private readonly Factory _factory;

    public VetsE2ETests(Factory factory)
    {
        _factory = factory;
        _ = factory.Host;
    }

    public void Dispose() { }

    private HttpClient Client(Guid userId, string role, Guid? headerFarm)
    {
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(userId, new[] { role });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerFarm.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());
        return client;
    }

    private sealed class VetDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    [Fact]
    public async Task Vets_AreFarmScoped()
    {
        var client = Client(JwtTokenHelper.TestUserId, "FarmManager", _factory.FarmId);

        var foreign = await client.GetAsync(
            $"/api/farm/{_factory.FarmId}/vets/{_factory.ForeignVetId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var list = await client.GetAsync(
            $"/api/farm/{_factory.FarmId}/vets?includeInactive=true");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var vets = await list.Content.ReadFromJsonAsync<List<VetDto>>();
        Assert.NotNull(vets);
        Assert.DoesNotContain(vets!, vet => vet.Name == "Dr. Foreign");
    }

    [Fact]
    public async Task NonMember_IsDeniedReadsAndWrites()
    {
        var client = Client(_factory.NonMemberUserId, "FarmManager", _factory.FarmId);

        var read = await client.GetAsync($"/api/farm/{_factory.FarmId}/vets");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var write = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/vets",
            new { name = "Dr. Intruder" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task AnyFarmMemberCanWrite_AndDuplicateNamesAreRejectedCaseInsensitively()
    {
        var client = Client(_factory.AccountantUserId, "Accountant", _factory.FarmId);

        var first = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/vets",
            new { name = "Dr. Safety" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/vets",
            new { name = "  dr. SAFETY  " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await client.GetAsync(
            $"/api/farm/{_factory.FarmId}/vets?includeInactive=true");
        var vets = await list.Content.ReadFromJsonAsync<List<VetDto>>();
        Assert.Single(vets!, vet => vet.Name == "Dr. Safety");
    }
}
