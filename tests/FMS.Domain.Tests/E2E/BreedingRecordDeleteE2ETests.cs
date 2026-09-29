using System.Net;
using System.Text.Json;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Deleting the mating a gestation record was created from.
///
/// <para>
/// The farm's own data already has this shape: the seeded mating is the reference the confirmed
/// gestation record hangs off, and <c>GestationRecords.BreedingRecordId</c> is a Restrict foreign
/// key. The delete therefore fails inside the database, and before this the client was shown a
/// generic 500 with the record still on screen. The refusal belongs up front, in the project's own
/// words, naming what is in the way (see <c>ConfigurationService.InUse</c>).
/// </para>
///
/// <para>
/// A link from a birth record does <em>not</em> block: <c>BirthRecords.BreedingRecordId</c> is
/// nullable and declared <c>SetNull</c>, so deleting the mating only unlinks it. Both halves are
/// asserted, because a guard that refuses whenever <em>anything</em> points at the row would be as
/// wrong as no guard at all.
/// </para>
/// </summary>
public class BreedingRecordDeleteE2ETests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BreedingRecordDeleteE2ETests(TestWebApplicationFactory factory) => _factory = factory;

    private Guid FarmId => _factory.SeedData!.FarmId;

    private HttpClient GetClient()
    {
        _ = _factory.Host; // creating the host is what seeds the database
        return _factory.CreateAuthenticatedClient(FarmId);
    }

    private sealed record Mating(Guid Id, Guid SireId, Guid DamId, string SireTag, string DamTag, DateTime BreedingDate);

    /// <summary>
    /// A mating of two of the farm's own animals, with the given records hanging off it.
    /// </summary>
    private async Task<Mating> SeedMatingAsync(int gestationRecords = 0, int birthRecords = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();

        var animals = await db.Animals
            .Where(a => a.FarmId == FarmId)
            .OrderBy(a => a.TagNumber)
            .Take(2)
            .Select(a => new { a.Id, a.TagNumber })
            .ToListAsync();
        Assert.Equal(2, animals.Count);

        var mating = new BreedingRecord
        {
            Id = Guid.NewGuid(),
            FarmId = FarmId,
            SireId = animals[0].Id,
            DamId = animals[1].Id,
            BreedingDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Method = BreedingMethod.Natural,
            Result = BreedingResult.Confirmed
        };
        db.BreedingRecords.Add(mating);

        for (var index = 0; index < gestationRecords; index++)
        {
            db.GestationRecords.Add(new GestationRecord
            {
                Id = Guid.NewGuid(),
                FarmId = FarmId,
                BreedingRecordId = mating.Id,
                AnimalId = mating.DamId,
                ConfirmedDate = mating.BreedingDate.AddDays(21),
                ExpectedDeliveryDate = mating.BreedingDate.AddDays(283),
                CurrentStage = GestationStage.Late
            });
        }

        for (var index = 0; index < birthRecords; index++)
        {
            db.BirthRecords.Add(new BirthRecord
            {
                Id = Guid.NewGuid(),
                FarmId = FarmId,
                DamId = mating.DamId,
                BreedingRecordId = mating.Id,
                BirthDate = mating.BreedingDate.AddDays(283),
                OffspringCount = 1
            });
        }

        await db.SaveChangesAsync();

        return new Mating(mating.Id, mating.SireId, mating.DamId, animals[0].TagNumber, animals[1].TagNumber, mating.BreedingDate);
    }

    private static string? MessageKey(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Message-Key", out var values) ? values.First() : null;

    private async Task<bool> MatingExistsAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await db.BreedingRecords.AnyAsync(br => br.Id == id);
    }

    /// <summary>
    /// The key the API just sent, checked against every bundle the client ships: a key the client
    /// cannot render falls back to the server's English text silently.
    /// </summary>
    private static async Task AssertKeyIsTranslatedEverywhere(string? key)
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

        foreach (var locale in new[] { "en", "es", "ar" })
        {
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
    }

    [Fact]
    public async Task DeletingAMatingAGestationRecordHangsOff_IsRefused_WithTheBlockerNamed()
    {
        var client = GetClient();
        var mating = await SeedMatingAsync(gestationRecords: 1);

        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{mating.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        // The project's own shape: what could not be deleted, what is in the way, and the remedy.
        Assert.Contains("Cannot delete breeding record", body);
        Assert.Contains($"{mating.SireTag} and {mating.DamTag} on 2026-05-01", body);
        Assert.Contains("still used by 1 gestation record", body);
        Assert.Contains("Delete that gestation record first", body);

        // The refusal is not the delete: the record is untouched.
        Assert.True(await MatingExistsAsync(mating.Id));

        var key = MessageKey(response);
        Assert.Equal("validation.breeding.recordInUse", key);
        await AssertKeyIsTranslatedEverywhere(key);
    }

    [Fact]
    public async Task TheRefusal_NamesTheBlockingRecords_RatherThanOnlyCountingThem()
    {
        var client = GetClient();
        var mating = await SeedMatingAsync(gestationRecords: 2);

        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{mating.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var expected = await db.GestationRecords
            .Where(gr => gr.BreedingRecordId == mating.Id)
            .Select(gr => new { gr.Id, gr.Animal.TagNumber, gr.ExpectedDeliveryDate })
            .ToListAsync();
        Assert.Equal(2, expected.Count);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // The sentence is still there, still plain: a caller that never learned the envelope
        // reads exactly the message it always read.
        Assert.Equal("Conflict", root.GetProperty("title").GetString());
        Assert.Contains("Cannot delete breeding record", root.GetProperty("detail").GetString());

        // The rows: one per gestation record, each routable and recognisable.
        var blockers = root.GetProperty("blockers");
        Assert.Equal(2, blockers.GetArrayLength());
        foreach (var blocker in blockers.EnumerateArray())
        {
            var id = blocker.GetProperty("id").GetGuid();
            var row = Assert.Single(expected, e => e.Id == id);
            Assert.Equal("gestationRecord", blocker.GetProperty("kind").GetString());
            var label = blocker.GetProperty("label").GetString();
            Assert.Contains(row.TagNumber, label);
            Assert.Contains(row.ExpectedDeliveryDate.ToString("yyyy-MM-dd"), label);
        }
    }

    [Fact]
    public async Task SeveralGestationRecords_AreCounted()
    {
        var client = GetClient();
        var mating = await SeedMatingAsync(gestationRecords: 2);

        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{mating.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("still used by 2 gestation records", body);
        Assert.Contains("Delete those gestation records first", body);
        Assert.True(await MatingExistsAsync(mating.Id));
    }

    [Fact]
    public async Task DeletingAMatingNothingReferences_Succeeds()
    {
        var client = GetClient();
        var mating = await SeedMatingAsync();

        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{mating.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(MessageKey(response));
        Assert.False(await MatingExistsAsync(mating.Id));
    }

    [Fact]
    public async Task DeletingAMatingOnlyABirthRecordLinksTo_Succeeds()
    {
        var client = GetClient();
        var mating = await SeedMatingAsync(birthRecords: 1);

        // The birth record's link is SetNull by schema, so nothing is destroyed and nothing blocks.
        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{mating.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await MatingExistsAsync(mating.Id));
    }

    [Fact]
    public async Task DeletingAMatingThatIsNotThere_IsStillNotFound()
    {
        var client = GetClient();

        var response = await client.DeleteAsync($"/api/farm/{FarmId}/breeding-records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Breeding record not found", await response.Content.ReadAsStringAsync());
    }
}
