using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The suggestions endpoint is read-only, farm-scoped, and limited to a closed
/// whitelist. These tests run the real FarmContextMiddleware so the outside-farm
/// case proves the production authorization path, not a permissive stub.
/// </summary>
public class SuggestionsE2ETests : IClassFixture<SuggestionsE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        public Guid FarmId { get; private set; }
        public Guid ForeignFarmId { get; private set; }

        protected override bool UsesRealFarmContextMiddleware => true;

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            FarmId = seed.FarmId;

            var animalId = await db.Animals
                .Where(animal => animal.FarmId == seed.FarmId)
                .Select(animal => animal.Id)
                .FirstAsync();

            var baseDate = DateTime.UtcNow.Date;

            // Old values fill the cap; the two named values are newest so the
            // ordering assertion does not depend on which conditions survive it.
            for (var i = 1; i <= 55; i++)
            {
                db.MedicalRecords.Add(new MedicalRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    AnimalId = animalId,
                    Symptoms = "Seeded",
                    Diagnosis = $"Condition-{i:D2}",
                    DateRecorded = baseDate.AddDays(-i),
                    Status = MedicalRecordStatus.Open
                });
            }

            db.MedicalRecords.AddRange(
                new MedicalRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    AnimalId = animalId,
                    Symptoms = "Seeded",
                    Diagnosis = "Mastitis",
                    DateRecorded = baseDate.AddDays(1),
                    Status = MedicalRecordStatus.Open
                },
                new MedicalRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    AnimalId = animalId,
                    Symptoms = "Seeded",
                    Diagnosis = "Lameness",
                    Dosage = "10 ml",
                    DateRecorded = baseDate.AddDays(2),
                    Status = MedicalRecordStatus.Open
                },
                new MedicalRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    AnimalId = animalId,
                    Symptoms = "Seeded",
                    Diagnosis = null,
                    Dosage = "5 ml",
                    DateRecorded = baseDate.AddDays(3),
                    Status = MedicalRecordStatus.Open
                });

            db.Suppliers.AddRange(
                new Supplier
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    Name = "Agro Vet",
                    ProductsSupplied = "Vaccines, Dewormers",
                    ModifiedAt = baseDate
                },
                new Supplier
                {
                    Id = Guid.NewGuid(),
                    FarmId = seed.FarmId,
                    Name = "Feed Store",
                    ProductsSupplied = "Mineral blocks",
                    ModifiedAt = baseDate.AddDays(1)
                });

            // Same account, but the test user has no UserFarm row for this farm.
            var foreignFarm = new Farm
            {
                Id = Guid.NewGuid(),
                AccountId = seed.AccountId,
                Name = "Foreign Farm"
            };
            db.Farms.Add(foreignFarm);
            db.MedicalRecords.Add(new MedicalRecord
            {
                Id = Guid.NewGuid(),
                FarmId = foreignFarm.Id,
                AnimalId = animalId,
                Symptoms = "Seeded",
                Diagnosis = "ForeignOnly",
                DateRecorded = baseDate.AddDays(10),
                Status = MedicalRecordStatus.Open
            });
            db.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                FarmId = foreignFarm.Id,
                Name = "Foreign Supplier",
                ProductsSupplied = "Foreign products",
                ModifiedAt = baseDate.AddDays(10)
            });

            await db.SaveChangesAsync();
            ForeignFarmId = foreignFarm.Id;
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public SuggestionsE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _ = factory.Host; // seeding sets FarmId/ForeignFarmId before the first test reads them
        _output = output;
    }

    public void Dispose() { }

    private HttpClient ClientWithHeader(Guid? headerFarm)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(JwtTokenHelper.TestUserId, new[] { "FarmManager" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerFarm.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());
        }
        return client;
    }

    [Fact]
    public async Task MedicalDiagnosis_IsScopedToTheFarm_AndOrderedByRecentUse()
    {
        var client = ClientWithHeader(_factory.FarmId);

        var response = await client.GetAsync($"/api/farms/{_factory.FarmId}/suggestions/medicalDiagnosis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(values);
        Assert.Equal("Lameness", values![0]);
        Assert.Equal("Mastitis", values[1]);
        Assert.DoesNotContain("ForeignOnly", values);
    }

    [Fact]
    public async Task SuggestionQueries_ReturnAtMostFiftyValues()
    {
        var client = ClientWithHeader(_factory.FarmId);

        var response = await client.GetAsync($"/api/farms/{_factory.FarmId}/suggestions/medicalDiagnosis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(values);
        Assert.Equal(50, values!.Count);
        Assert.Contains("Condition-01", values);
        Assert.DoesNotContain("Condition-49", values);
    }

    [Fact]
    public async Task SupplierProducts_AreScopedToTheFarm()
    {
        var client = ClientWithHeader(_factory.FarmId);

        var response = await client.GetAsync($"/api/farms/{_factory.FarmId}/suggestions/supplierProductsSupplied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(values);
        Assert.Contains("Vaccines, Dewormers", values);
        Assert.Contains("Mineral blocks", values);
        Assert.DoesNotContain("Foreign products", values);
    }

    [Fact]
    public async Task UnknownField_ReturnsNotFound()
    {
        var client = ClientWithHeader(_factory.FarmId);

        var response = await client.GetAsync($"/api/farms/{_factory.FarmId}/suggestions/passwordHash");

        _output.WriteLine($"unknown field -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserOutsideTheFarm_IsDenied()
    {
        var client = ClientWithHeader(null);

        var response = await client.GetAsync($"/api/farms/{_factory.ForeignFarmId}/suggestions/medicalDiagnosis");

        _output.WriteLine($"foreign farm -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
