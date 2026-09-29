using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FMS.Application.Animal;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The parentage rules over the real HTTP pipeline: a sire has to be male, a dam female, and
/// neither may be the animal itself. Driven through the test server rather than the service
/// because half of what is claimed lives outside the service — the 400 the client's error path
/// reads, the <c>X-Message-Key</c> header the controller has to attach (a keyed <c>Error</c>
/// nobody forwards is English on screen), and the fact that the refusal happens before anything
/// is written.
///
/// <para>
/// The escape hatch is asserted here as well as at the service level, because it is the part a
/// later tightening is most likely to break: the farm already holds one impossible parent (a
/// female recorded as the sire of TAG-0079 Boocho), and the rule is gated on the request
/// changing that parent so the record stays editable.
/// </para>
/// </summary>
public class AnimalParentageE2ETests : IClassFixture<TestWebApplicationFactory>
{
    private static int _seedCounter;

    private readonly TestWebApplicationFactory _factory;

    public AnimalParentageE2ETests(TestWebApplicationFactory factory) => _factory = factory;

    private Guid FarmId => _factory.SeedData!.FarmId;

    private HttpClient GetClient()
    {
        _ = _factory.Host; // creating the host is what seeds the database
        return _factory.CreateAuthenticatedClient(FarmId);
    }

    private sealed record ParentageAnimals(Guid CowId, Guid BullId, Guid SecondCowId, Guid CalfId, string CalfTag);

    /// <summary>
    /// A bull, two cows, and a calf whose recorded sire is the first cow — the farm's real
    /// mistake, reproduced in the seeded farm's own tables.
    /// </summary>
    private ParentageAnimals SeedParentage()
    {
        var seed = _factory.SeedData!;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();

        var run = Interlocked.Increment(ref _seedCounter);
        Animal Add(string tag, Guid sexId, Guid? sireId = null)
        {
            var animal = new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                TagNumber = tag,
                AnimalTypeId = seed.AnimalTypeId,
                SexOptionId = sexId,
                AnimalStatusId = seed.ActiveStatusId,
                SireId = sireId
            };
            db.Animals.Add(animal);
            return animal;
        }

        var cow = Add($"E2E-COW-{run}", seed.SexFemaleId);
        var bull = Add($"E2E-BULL-{run}", seed.SexMaleId);
        var secondCow = Add($"E2E-COW2-{run}", seed.SexFemaleId);
        var calf = Add($"E2E-CALF-{run}", seed.SexFemaleId, cow.Id);
        db.SaveChanges();

        return new ParentageAnimals(cow.Id, bull.Id, secondCow.Id, calf.Id, calf.TagNumber);
    }

    private CreateAnimalRequest NewCalf(string tag)
    {
        var seed = _factory.SeedData!;
        return new CreateAnimalRequest
        {
            TagNumber = tag,
            AnimalTypeId = seed.AnimalTypeId,
            SexOptionId = seed.SexFemaleId,
            AnimalStatusId = seed.ActiveStatusId
        };
    }

    private static string? MessageKey(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Message-Key", out var values) ? values.First() : null;

    private static async Task AssertKeyIsTranslated(string? key, string locale)
    {
        Assert.NotNull(key);
        var segments = key!.Split('.');
        Assert.Equal("validation", segments[0]);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "client", "src", "i18n", "locales")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var file = Path.Combine(
            directory!.FullName, "client", "src", "i18n", "locales", locale, $"{segments[0]}.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));

        var element = document.RootElement;
        for (var index = 1; index < segments.Length; index++)
            Assert.True(
                element.TryGetProperty(segments[index], out element),
                $"{key} is missing from {locale}/{segments[0]}.json");

        Assert.False(string.IsNullOrWhiteSpace(element.GetString()), $"{key} is blank in {locale}");
    }

    /// <summary>
    /// The key the API just sent, checked against every bundle the client ships. This is the
    /// join the header design depends on: a key the client cannot render falls back to the
    /// server's English text silently, which is the failure mode the i18n work exists to remove.
    /// </summary>
    private static async Task AssertKeyIsTranslatedEverywhere(string? key)
    {
        foreach (var locale in new[] { "en", "es", "ar" })
            await AssertKeyIsTranslated(key, locale);
    }

    [Fact]
    public async Task CreatingAnAnimalWithAFemaleSire_IsRefused_WithATranslatedKey()
    {
        var client = GetClient();
        var animals = SeedParentage();
        var request = NewCalf("E2E-NEW-SIRE");
        request.SireId = animals.CowId;

        var response = await client.PostAsJsonAsync($"/api/farm/{FarmId}/animals", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The sire must be male", await response.Content.ReadAsStringAsync());
        Assert.Equal("validation.animal.sireMustBeMale", MessageKey(response));
        await AssertKeyIsTranslatedEverywhere(MessageKey(response));

        // Refused before anything was written: no half-created animal is left behind.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        Assert.False(await db.Animals.AnyAsync(a => a.FarmId == FarmId && a.TagNumber == "E2E-NEW-SIRE"));
    }

    [Fact]
    public async Task CreatingAnAnimalWithAMaleDam_IsRefused_WithATranslatedKey()
    {
        var client = GetClient();
        var animals = SeedParentage();
        var request = NewCalf("E2E-NEW-DAM");
        request.DamId = animals.BullId;

        var response = await client.PostAsJsonAsync($"/api/farm/{FarmId}/animals", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The dam must be female", await response.Content.ReadAsStringAsync());
        Assert.Equal("validation.animal.damMustBeFemale", MessageKey(response));
        await AssertKeyIsTranslatedEverywhere(MessageKey(response));
    }

    [Fact]
    public async Task CreatingAnAnimalWithAMaleSireAndAFemaleDam_Succeeds()
    {
        var client = GetClient();
        var animals = SeedParentage();
        var request = NewCalf("E2E-NEW-OK");
        request.SireId = animals.BullId;
        request.DamId = animals.CowId;

        var response = await client.PostAsJsonAsync($"/api/farm/{FarmId}/animals", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(MessageKey(response));
    }

    [Fact]
    public async Task UpdatingTheRecordThatAlreadyNamesAFemaleAsItsSire_Saves()
    {
        var client = GetClient();
        var animals = SeedParentage();

        // Exactly what the edit form sends back: the whole record, impossible parent included,
        // with the name corrected.
        var response = await client.PutAsJsonAsync(
            $"/api/farm/{FarmId}/animals/{animals.CalfId}",
            new UpdateAnimalRequest
            {
                TagNumber = animals.CalfTag,
                Name = "Boocho",
                SexOptionId = _factory.SeedData!.SexFemaleId,
                SireId = animals.CowId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var stored = await db.Animals.SingleAsync(a => a.Id == animals.CalfId);
        Assert.Equal("Boocho", stored.Name);
        // The impossible parent is untouched: fixing it is the user's choice, not the save's.
        Assert.Equal(animals.CowId, stored.SireId);
    }

    [Fact]
    public async Task UpdatingAnAnimalToNameAFemaleAsItsSire_IsRefused_WithATranslatedKey()
    {
        var client = GetClient();
        var animals = SeedParentage();

        var response = await client.PutAsJsonAsync(
            $"/api/farm/{FarmId}/animals/{animals.CalfId}",
            new UpdateAnimalRequest
            {
                TagNumber = animals.CalfTag,
                SexOptionId = _factory.SeedData!.SexFemaleId,
                SireId = animals.SecondCowId
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.animal.sireMustBeMale", MessageKey(response));
        await AssertKeyIsTranslatedEverywhere(MessageKey(response));

        // The rejected change was not half-applied.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        Assert.Equal(animals.CowId, (await db.Animals.SingleAsync(a => a.Id == animals.CalfId)).SireId);
    }

    [Fact]
    public async Task ClearingAFemaleSire_IsAllowed_SoTheMistakeCanBeCorrected()
    {
        var client = GetClient();
        var animals = SeedParentage();

        var response = await client.PutAsJsonAsync(
            $"/api/farm/{FarmId}/animals/{animals.CalfId}",
            new UpdateAnimalRequest
            {
                TagNumber = animals.CalfTag,
                Name = "Boocho",
                SexOptionId = _factory.SeedData!.SexFemaleId,
                SireId = null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        Assert.Null((await db.Animals.SingleAsync(a => a.Id == animals.CalfId)).SireId);
    }

    /// <summary>
    /// An animal as its own parent keeps the answer this endpoint has always given — the id is
    /// "not another animal's", which is a 404, exactly as an unknown sire id is — but it now
    /// carries the key as well. Kept rather than tightened to a 400 on purpose: the shape of a
    /// pre-existing refusal is not something a parentage rule should quietly change.
    /// </summary>
    [Fact]
    public async Task UpdatingAnAnimalToBeItsOwnSire_IsRefused()
    {
        var client = GetClient();
        var animals = SeedParentage();

        var response = await client.PutAsJsonAsync(
            $"/api/farm/{FarmId}/animals/{animals.CalfId}",
            new UpdateAnimalRequest
            {
                TagNumber = animals.CalfTag,
                SexOptionId = _factory.SeedData!.SexFemaleId,
                SireId = animals.CalfId
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sire animal not found", await response.Content.ReadAsStringAsync());
        Assert.Equal("validation.animal.sireNotFound", MessageKey(response));
        await AssertKeyIsTranslatedEverywhere(MessageKey(response));
    }
}
