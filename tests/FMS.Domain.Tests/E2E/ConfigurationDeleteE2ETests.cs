using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Configuration deletes over the real HTTP pipeline. An in-use lookup must answer 409 with a
/// reason the UI can show (it used to fail inside the database and surface as a generic 500 with
/// the row still listed), and the same lookup must still delete once nothing references it.
/// </summary>
public class ConfigurationDeleteE2ETests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConfigurationDeleteE2ETests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient GetClient()
    {
        _ = _factory.Host; // creating the host is what seeds the database
        return _factory.CreateAuthenticatedClient(_factory.SeedData.FarmId);
    }

    [Fact]
    public async Task DeletingAnInUseLocationType_Returns409WithReason_ThenDeletesOnceUnreferenced()
    {
        var client = GetClient();
        var farmId = _factory.SeedData.FarmId;

        var typeResponse = await client.PostAsJsonAsync(
            $"/api/farm/{farmId}/configuration/location-types", new { name = "E2E Shed" });
        Assert.Equal(HttpStatusCode.Created, typeResponse.StatusCode);
        var locationType = await typeResponse.Content.ReadFromJsonAsync<LocationTypeDto>();
        Assert.NotNull(locationType);

        var locationResponse = await client.PostAsJsonAsync(
            $"/api/farm/{farmId}/configuration/locations",
            new { name = "E2E Shed A", locationTypeId = locationType!.Id });
        Assert.Equal(HttpStatusCode.Created, locationResponse.StatusCode);
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(location);

        // The refusal must be a conflict with a readable reason, not a 500.
        var refusal = await client.DeleteAsync(
            $"/api/farm/{farmId}/configuration/location-types/{locationType.Id}");

        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        var body = await refusal.Content.ReadAsStringAsync();
        Assert.Contains("Cannot delete location type 'E2E Shed'", body);
        Assert.Contains("1 location", body);

        // The refused lookup is still there, and removing the reference unblocks it.
        var referenceRemoved = await client.DeleteAsync(
            $"/api/farm/{farmId}/configuration/locations/{location!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, referenceRemoved.StatusCode);

        var deletion = await client.DeleteAsync(
            $"/api/farm/{farmId}/configuration/location-types/{locationType.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        var remaining = await client.GetFromJsonAsync<List<LocationTypeDto>>(
            $"/api/farm/{farmId}/configuration/location-types");
        Assert.NotNull(remaining);
        Assert.DoesNotContain(remaining!, lt => lt.Id == locationType.Id);
    }

    [Fact]
    public async Task DeletingAMissingLookup_StillReturns404()
    {
        var client = GetClient();

        var response = await client.DeleteAsync(
            $"/api/farm/{_factory.SeedData.FarmId}/configuration/location-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The reported symptom: a delete answers success, then the row is still listed — once from the
    /// breed list itself, and once from the animal-type payload that embeds its breeds and count.
    /// </summary>
    [Fact]
    public async Task DeletingABreed_RemovesItFromTheBreedListAndFromTheAnimalTypeImmediately()
    {
        var client = GetClient();
        var farmId = _factory.SeedData.FarmId;

        var typeResponse = await client.PostAsJsonAsync(
            $"/api/farm/{farmId}/configuration/animal-types", new { name = "E2E Poultry" });
        Assert.Equal(HttpStatusCode.Created, typeResponse.StatusCode);
        var animalType = await typeResponse.Content.ReadFromJsonAsync<AnimalTypeDto>();
        Assert.NotNull(animalType);

        var breedResponse = await client.PostAsJsonAsync(
            $"/api/farm/{farmId}/configuration/breeds",
            new { name = "E2E Layer", animalTypeId = animalType!.Id, averageGestationDays = 21 });
        Assert.Equal(HttpStatusCode.Created, breedResponse.StatusCode);
        var breed = await breedResponse.Content.ReadFromJsonAsync<BreedDto>();
        Assert.NotNull(breed);

        // Make sure the very list that will have to drop the row is populated first.
        var beforeDelete = await client.GetFromJsonAsync<List<BreedDto>>(
            $"/api/farm/{farmId}/configuration/breeds");
        Assert.Contains(beforeDelete!, b => b.Id == breed!.Id);

        var deletion = await client.DeleteAsync($"/api/farm/{farmId}/configuration/breeds/{breed!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        // The row must not come back from a stale cached list on the next read.
        var breeds = await client.GetFromJsonAsync<List<BreedDto>>(
            $"/api/farm/{farmId}/configuration/breeds");
        Assert.NotNull(breeds);
        Assert.DoesNotContain(breeds!, b => b.Id == breed.Id);

        // Nor linger inside the animal-type payload, which embeds its breeds and their count.
        var types = await client.GetFromJsonAsync<List<AnimalTypeDto>>(
            $"/api/farm/{farmId}/configuration/animal-types");
        var type = Assert.Single(types!, t => t.Id == animalType.Id);
        Assert.DoesNotContain(type.Breeds, b => b.Id == breed.Id);
    }

    private sealed record AnimalTypeDto(Guid Id, string Name, List<BreedDto> Breeds);

    private sealed record BreedDto(Guid Id, string Name, Guid AnimalTypeId, int AverageGestationDays);

    private sealed record LocationTypeDto(Guid Id, string Name);

    private sealed record LocationDto(Guid Id, string Name, Guid LocationTypeId);
}
