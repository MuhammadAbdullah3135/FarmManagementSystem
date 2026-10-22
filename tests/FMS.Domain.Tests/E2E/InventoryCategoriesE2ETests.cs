using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Category reads follow farm membership; writes use the inventory write roles. The
/// real FarmContextMiddleware runs, so the farm-scoping assertions exercise production
/// membership enforcement rather than a permissive stub.
/// </summary>
public class InventoryCategoriesE2ETests : IClassFixture<InventoryCategoriesE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        public Guid FarmId { get; private set; }
        public Guid ForeignFarmId { get; private set; }
        public Guid ForeignCategoryId { get; private set; }
        public Guid AccountantUserId { get; private set; }

        protected override bool UsesRealFarmContextMiddleware => true;

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            FarmId = seed.FarmId;

            AccountantUserId = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = AccountantUserId,
                AccountId = seed.AccountId,
                Email = "accountant@test.com",
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
            var foreignCategory = new InventoryCategory
            {
                Id = Guid.NewGuid(),
                FarmId = foreignFarm.Id,
                Name = "Foreign Supplies"
            };
            db.Farms.Add(foreignFarm);
            db.InventoryCategories.Add(foreignCategory);

            await db.SaveChangesAsync();
            ForeignFarmId = foreignFarm.Id;
            ForeignCategoryId = foreignCategory.Id;
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public InventoryCategoriesE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _ = factory.Host;
        _output = output;
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

    private sealed class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private sealed class ItemDto
    {
        public Guid Id { get; set; }
        public Guid? CategoryId { get; set; }
        public string? Category { get; set; }
    }

    [Fact]
    public async Task Categories_AreFarmScoped()
    {
        var client = Client(JwtTokenHelper.TestUserId, "FarmManager", _factory.FarmId);

        var foreign = await client.GetAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories/{_factory.ForeignCategoryId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var list = await client.GetAsync($"/api/farm/{_factory.FarmId}/inventory/categories?includeInactive=true");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var categories = await list.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(categories);
        Assert.DoesNotContain(categories!, category => category.Name == "Foreign Supplies");
    }

    [Fact]
    public async Task Accountant_CanRead_ButCannotWriteCategories()
    {
        var client = Client(_factory.AccountantUserId, "Accountant", _factory.FarmId);

        var read = await client.GetAsync($"/api/farm/{_factory.FarmId}/inventory/categories");
        _output.WriteLine($"accountant read -> {read.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories",
            new { name = "Accountant Category" });
        _output.WriteLine($"accountant write -> {write.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task DuplicateNames_AreRejectedCaseInsensitively()
    {
        var client = Client(JwtTokenHelper.TestUserId, "FarmManager", _factory.FarmId);

        var first = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories",
            new { name = "Safety gear" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories",
            new { name = "  safety GEAR  " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await client.GetAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories?includeInactive=true");
        var categories = await list.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.Single(categories!, category => category.Name == "Safety gear");
    }

    [Fact]
    public async Task CreatingAndUpdatingAnItem_KeepsTheStringAndIdInSync()
    {
        var client = Client(JwtTokenHelper.TestUserId, "FarmManager", _factory.FarmId);

        var equipment = await (await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories",
            new { name = "Equipment" })).Content.ReadFromJsonAsync<CategoryDto>();
        var consumables = await (await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory/categories",
            new { name = "Consumables" })).Content.ReadFromJsonAsync<CategoryDto>();

        var create = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory-items",
            new
            {
                name = "Fence wire",
                categoryId = equipment!.Id,
                category = "not the linked name",
                unit = "roll",
                quantity = 0,
                reorderLevel = 1,
                unitCost = 10
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var item = await create.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal(equipment.Id, item!.CategoryId);
        Assert.Equal("Equipment", item.Category);

        var update = await client.PutAsJsonAsync(
            $"/api/farm/{_factory.FarmId}/inventory-items/{item.Id}",
            new
            {
                name = "Fence wire",
                categoryId = consumables!.Id,
                category = "still not the linked name",
                unit = "roll",
                reorderLevel = 1,
                unitCost = 10
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal(consumables.Id, updated!.CategoryId);
        Assert.Equal("Consumables", updated.Category);
    }
}
