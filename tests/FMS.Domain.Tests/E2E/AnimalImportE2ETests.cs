using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The import over HTTP, through the real controllers and the real
/// FarmContextMiddleware — the same farm-scoped route treatment every other
/// <c>/api/farm/{farmId}/…</c> endpoint gets, and one more check that a caller cannot
/// write into a farm they do not belong to.
/// </summary>
public class AnimalImportE2ETests : IClassFixture<AnimalImportE2ETests.Factory>
{
    private const string ValidCsv =
        "tagNumber,name,animalType,sex,status\n" +
        "IMPE2E-001,Bella,Cattle,Female,Active\n" +
        "IMPE2E-002,,Cattle,Male,Active\n";

    private readonly Factory _factory;

    public AnimalImportE2ETests(Factory factory) => _factory = factory;

    /// <summary>
    /// The seeded farm. Reading it forces the host to be built, which is what runs the
    /// seed (and fills in <c>ForeignFarmId</c>).
    /// </summary>
    private Guid FarmId
    {
        get
        {
            _ = _factory.Host;
            return _factory.SeedData.FarmId;
        }
    }

    private HttpClient Client(Guid farmId) => _factory.CreateAuthenticatedClient(farmId);

    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>A farm the seeded test user has no membership in.</summary>
        public Guid ForeignFarmId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            // No location type and no sex options: the shared seed owns both. It adds
            // the location type its required FK needs, and its sex options belong to
            // the farm. This factory used to supply its own copies to work around
            // those columns being empty — a state PostgreSQL rejects outright, and
            // since the import resolves a name against the farm's own configuration,
            // the second pair under the same names made every row ambiguous.

            db.AgeCategories.Add(new AgeCategory
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                Name = "Adult",
                MinDays = 365,
                MaxDays = 9999
            });

            ForeignFarmId = Guid.NewGuid();
            db.Farms.Add(new Farm { Id = ForeignFarmId, AccountId = seed.AccountId, Name = "Foreign Farm" });

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Preview_ReportsTheFile_AndCreatesNothing()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        using var response = await PostFileAsync(client, farmId, "preview", Csv(ValidCsv));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<PreviewShape>();
        Assert.NotNull(preview);
        Assert.Equal(2, preview!.TotalRows);
        Assert.Equal(2, preview.ValidRowCount);
        Assert.Equal(0, preview.InvalidRowCount);

        // The wizard builds its mapping step from this response.
        Assert.Contains("IMPE2E-001", preview.SampleValidRows[0].Values["tagNumber"]);

        Assert.Equal(8, await SeedAnimalCountAsync(farmId));
    }

    [Fact]
    public async Task Commit_ImportsTheFile_AndTheAnimalsShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        using var response = await PostFileAsync(client, farmId, "commit", Csv(ValidCsv));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(2, commit!.ImportedCount);
        Assert.Empty(commit.InvalidRows);

        var list = await client.GetFromJsonAsync<AnimalListShape>(
            $"/api/farm/{farmId}/animals?search=IMPE2E");

        Assert.NotNull(list);
        Assert.Equal(
            new[] { "IMPE2E-001", "IMPE2E-002" },
            list!.Items.Select(item => item.TagNumber).OrderBy(tag => tag));
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_ImportsNothingAndReturnsEveryProblem()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var csv = "tagNumber,animalType,sex,status\n" +
                  "IMPE2E-101,Cattle,Female,Active\n" +
                  "IMPE2E-102,Unicorns,Female,Active\n";

        using var response = await PostFileAsync(client, farmId, "commit", Csv(csv));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(2, commit!.TotalRows);
        Assert.Equal(0, commit.ImportedCount);

        var invalid = Assert.Single(commit.InvalidRows);
        Assert.Equal(3, invalid.RowNumber);
        Assert.Contains("Unicorns", invalid.Errors[0].Message);

        // The valid row of the file was not written either.
        Assert.Equal(8, await SeedAnimalCountAsync(farmId));
    }

    [Fact]
    public async Task Preview_WithADateFormatInTheMapping_ResolvesAnAmbiguousDate()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var csv = "tagNumber,animalType,sex,status,dateOfBirth\nIMPE2E-201,Cattle,Female,Active,01/02/2023\n";

        using var withoutFormat = await PostFileAsync(client, farmId, "preview", Csv(csv));
        var ambiguous = await withoutFormat.Content.ReadFromJsonAsync<PreviewShape>();
        Assert.Equal(1, ambiguous!.InvalidRowCount);
        Assert.Contains("ambiguous", ambiguous.InvalidRows[0].Errors[0].Message);

        using var withFormat = await PostFileAsync(
            client, farmId, "preview", Csv(csv), mapping: "{\"dateFormat\":\"dd/MM/yyyy\"}");
        var resolved = await withFormat.Content.ReadFromJsonAsync<PreviewShape>();
        Assert.Equal(0, resolved!.InvalidRowCount);
    }

    [Fact]
    public async Task Commit_WithAMappingTheServerCannotRead_IsAValidationError()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        using var response = await PostFileAsync(client, farmId, "commit", Csv(ValidCsv), mapping: "{not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(8, await SeedAnimalCountAsync(farmId));
    }

    [Fact]
    public async Task Import_IntoAFarmTheCallerIsNotAMemberOf_IsForbiddenAndWritesNothing()
    {
        _ = _factory.Host;
        var foreignFarmId = _factory.ForeignFarmId;
        var client = Client(foreignFarmId);

        using var preview = await PostFileAsync(client, foreignFarmId, "preview", Csv(ValidCsv));
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);

        using var commit = await PostFileAsync(client, foreignFarmId, "commit", Csv(ValidCsv));
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);

        Assert.Equal(0, await SeedAnimalCountAsync(foreignFarmId));
    }

    [Fact]
    public async Task Import_WithNoToken_IsUnauthorized()
    {
        var farmId = FarmId;
        using var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Csv(ValidCsv)), "file", "animals.csv");

        using var response = await client.PostAsync($"/api/farm/{farmId}/animals/import/preview", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<int> SeedAnimalCountAsync(Guid farmId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await db.Animals.CountAsync(animal => animal.FarmId == farmId);
    }

    private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client,
        Guid farmId,
        string action,
        byte[] bytes,
        string? mapping = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "animals.csv");

        if (mapping is not null)
            content.Add(new StringContent(mapping, Encoding.UTF8, "application/json"), "mapping");

        return await client.PostAsync($"/api/farm/{farmId}/animals/import/{action}", content);
    }

    private sealed record PreviewShape(
        int TotalRows,
        int ValidRowCount,
        int InvalidRowCount,
        List<RowShape> InvalidRows,
        List<RowShape> SampleValidRows);

    private sealed record CommitShape(int TotalRows, int ImportedCount, List<RowShape> InvalidRows);

    private sealed record RowShape(int RowNumber, Dictionary<string, string> Values, List<ErrorShape> Errors);

    private sealed record ErrorShape(string Field, string Message);

    private sealed record AnimalListShape(List<AnimalItemShape> Items);

    private sealed record AnimalItemShape(string TagNumber);
}
