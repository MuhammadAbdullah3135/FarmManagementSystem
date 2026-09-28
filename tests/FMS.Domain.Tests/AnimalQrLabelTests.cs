using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Common;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// The QR label contract: what goes into the code, what a sheet of codes contains, and what the
/// reader is willing to accept back.
///
/// <para>
/// The payload is asserted as an exact string rather than "contains the id". A label is printed,
/// glued to an animal, and read months later by a camera that knows nothing about this codebase,
/// so the shape <em>is</em> the interface — and the client's parser mirrors it deliberately
/// (<c>client/src/offline/scanResolve.ts</c>). These tests are the server half of that agreement.
/// </para>
/// </summary>
public class AnimalQrLabelTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AnimalService CreateService(FmsDbContext context, string frontendBaseUrl = TestConfiguration.FrontendBaseUrl) =>
        new(context, new FixedCurrentUser(),
            new FMS.Infrastructure.Files.FileStorageService(Path.Combine(Path.GetTempPath(), "fms-test-uploads")),
            TestConfiguration.WithFrontend(frontendBaseUrl));

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record Seed(Guid FarmId, Guid ActiveStatusId, Guid SoldStatusId);

    private static async Task<Seed> SeedFarmAsync(FmsDbContext context, string name = "Farm")
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = name };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var active = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var sold = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sold", Category = AnimalStatusCategory.Terminal };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.AddRange(active, sold);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, active.Id, sold.Id);
    }

    private static async Task<FMS.Domain.Entities.Animal> AddAnimalAsync(
        FmsDbContext context, Seed seed, string tag, DateTime? createdAt = null, bool deleted = false,
        Guid? statusId = null, string? name = null)
    {
        var type = await context.AnimalTypes.AsNoTracking().FirstAsync(t => t.FarmId == seed.FarmId);
        var sex = await context.SexOptions.AsNoTracking().FirstAsync(s => s.FarmId == seed.FarmId);

        var animal = new FMS.Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            TagNumber = tag,
            Name = name,
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = statusId ?? seed.ActiveStatusId,
            DateOfBirth = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsDeleted = deleted
        };

        context.Animals.Add(animal);
        await context.SaveChangesAsync();
        return animal;
    }

    // ── The payload ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Label_IsTheConfiguredFrontendOriginPlusTheAnimalRoute()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var animal = await AddAnimalAsync(context, seed, "TL-001");
        var service = CreateService(context);

        var result = await service.GetQrLabelAsync(seed.FarmId, animal.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{TestConfiguration.FrontendBaseUrl}/dashboard/animals/{animal.Id}", result.Value!.Url);
    }

    [Fact]
    public async Task Detail_CarriesTheSamePayloadAsTheLabel_SoShowingACodeCostsNoSecondRequest()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var animal = await AddAnimalAsync(context, seed, "TL-002");
        var service = CreateService(context);

        var detail = (await service.GetAnimalByIdAsync(seed.FarmId, animal.Id)).Value!;
        var label = (await service.GetQrLabelAsync(seed.FarmId, animal.Id)).Value!;

        // One builder, two readers: the animal's own page and the sheet that prints its label
        // read the same string, so they cannot drift into describing different tags.
        Assert.Equal(label.Url, detail.QrUrl);
        Assert.Equal($"{TestConfiguration.FrontendBaseUrl}/dashboard/animals/{animal.Id}", detail.QrUrl);

        // And it follows the deployment, not the request: change the setting and the record's own
        // payload moves with the label's, which is what keeps a re-pointed frontend from leaving
        // codes on screens that disagree with the ones on tags.
        var elsewhere = CreateService(context, "https://other.example.com");
        var moved = (await elsewhere.GetAnimalByIdAsync(seed.FarmId, animal.Id)).Value!;
        Assert.Equal($"https://other.example.com/dashboard/animals/{animal.Id}", moved.QrUrl);
    }

    [Fact]
    public async Task Label_CarriesTheTagNumberAsTextAlongsideTheCode()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var animal = await AddAnimalAsync(context, seed, "TL-007", name: "Bessie");
        var service = CreateService(context);

        var label = (await service.GetQrLabelAsync(seed.FarmId, animal.Id)).Value!;

        // The code is opaque to a person; the label has to be readable without a phone.
        Assert.Equal("TL-007", label.TagNumber);
        Assert.Equal("Bessie", label.Name);
        Assert.Equal("Cow", label.AnimalTypeName);
    }

    [Fact]
    public async Task Label_UsesAConfiguredOriginWithATrailingSlashWithoutDoublingIt()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var animal = await AddAnimalAsync(context, seed, "TL-001");
        var service = CreateService(context, "https://farm.example.com/");

        var label = (await service.GetQrLabelAsync(seed.FarmId, animal.Id)).Value!;

        Assert.Equal($"https://farm.example.com/dashboard/animals/{animal.Id}", label.Url);
        Assert.DoesNotContain("//dashboard", label.Url);
    }

    [Fact]
    public async Task Label_ForAnotherFarmsAnimal_IsNotFound()
    {
        using var context = CreateContext();
        var mine = await SeedFarmAsync(context, "Mine");
        var theirs = await SeedFarmAsync(context, "Theirs");
        var stranger = await AddAnimalAsync(context, theirs, "XX-001");
        var service = CreateService(context);

        var result = await service.GetQrLabelAsync(mine.FarmId, stranger.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task Label_ForADeletedAnimal_IsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var animal = await AddAnimalAsync(context, seed, "TL-001", deleted: true);
        var service = CreateService(context);

        var result = await service.GetQrLabelAsync(seed.FarmId, animal.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    // ── The sheet ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sheet_PrintsExactlyThePageTheAnimalListIsShowing()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        for (var i = 1; i <= 7; i++)
            await AddAnimalAsync(context, seed, $"TL-{i:000}", createdAt: new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(context);
        var filter = new AnimalListFilter { Page = 2, PageSize = 3, SortBy = "tagnumber", SortDescending = false };

        var table = (await service.GetAnimalsAsync(seed.FarmId, filter)).Value!;
        var sheet = (await service.GetQrLabelsAsync(seed.FarmId, filter)).Value!;

        // A sheet printed from a filtered table has to be that table: same rows, same order,
        // same count. Anything else labels the animals the user did not select.
        Assert.Equal(table.TotalCount, sheet.TotalCount);
        Assert.Equal(table.Items.Select(a => a.Id), sheet.Items.Select(l => l.AnimalId));
        Assert.Equal(table.Items.Select(a => a.TagNumber), sheet.Items.Select(l => l.TagNumber));
    }

    [Fact]
    public async Task Sheet_HonoursTheSearchFilterAndBuildsEachPayload()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var wanted = await AddAnimalAsync(context, seed, "COW-101");
        await AddAnimalAsync(context, seed, "SHEEP-101");

        var service = CreateService(context);
        var sheet = (await service.GetQrLabelsAsync(seed.FarmId, new AnimalListFilter { Search = "COW" })).Value!;

        var label = Assert.Single(sheet.Items);
        Assert.Equal(wanted.Id, label.AnimalId);
        Assert.Equal($"{TestConfiguration.FrontendBaseUrl}/dashboard/animals/{wanted.Id}", label.Url);
    }

    [Fact]
    public async Task Sheet_BatchesByWhateverPageSizeTheCallerAsksFor()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        for (var i = 1; i <= 120; i++)
            await AddAnimalAsync(context, seed, $"TL-{i:000}", createdAt: new DateTime(2026, 1, 1).AddMinutes(i));

        var service = CreateService(context);
        var sheet = (await service.GetQrLabelsAsync(seed.FarmId, new AnimalListFilter { PageSize = 100 })).Value!;

        Assert.Equal(120, sheet.TotalCount);
        Assert.Equal(100, sheet.PageSize);
        Assert.Equal(100, sheet.Items.Count);
    }

    [Fact]
    public async Task Sheet_WithoutAPageSizeResolvesTheSamePageAsTheAnimalList()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        for (var i = 1; i <= 30; i++)
            await AddAnimalAsync(context, seed, $"TL-{i:000}", createdAt: new DateTime(2026, 1, 1).AddMinutes(i));

        var service = CreateService(context);
        var filter = new AnimalListFilter();

        var table = (await service.GetAnimalsAsync(seed.FarmId, filter)).Value!;
        var sheet = (await service.GetQrLabelsAsync(seed.FarmId, filter)).Value!;

        // A request that asks for nothing gets the list's default on both routes: a sheet whose
        // default was larger would quietly print a page the user was never shown.
        Assert.Equal(table.PageSize, sheet.PageSize);
        Assert.Equal(table.Items.Select(a => a.Id), sheet.Items.Select(l => l.AnimalId));
    }

    [Fact]
    public async Task Sheet_LeavesOutTerminalAnimalsUnlessTheyAreAskedFor()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await AddAnimalAsync(context, seed, "TL-001");
        await AddAnimalAsync(context, seed, "TL-002", statusId: seed.SoldStatusId);

        var service = CreateService(context);

        var activeOnly = (await service.GetQrLabelsAsync(seed.FarmId, new AnimalListFilter())).Value!;
        var everything = (await service.GetQrLabelsAsync(seed.FarmId, new AnimalListFilter { IncludeTerminal = true })).Value!;

        // Same rule as the animal table: a sold animal's tag is not reprinted by default, and
        // the sheet has no separate opinion about it.
        Assert.Equal("TL-001", Assert.Single(activeOnly.Items).TagNumber);
        Assert.Equal(2, everything.TotalCount);
    }

    [Fact]
    public async Task Sheet_ForAnotherFarm_IsEmpty()
    {
        using var context = CreateContext();
        var mine = await SeedFarmAsync(context, "Mine");
        var theirs = await SeedFarmAsync(context, "Theirs");
        await AddAnimalAsync(context, theirs, "XX-001");

        var service = CreateService(context);
        var sheet = (await service.GetQrLabelsAsync(mine.FarmId, new AnimalListFilter())).Value!;

        Assert.Equal(0, sheet.TotalCount);
        Assert.Empty(sheet.Items);
    }

    [Fact]
    public async Task Sheet_CapsThePageSizeAtTheListsCeiling()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await AddAnimalAsync(context, seed, "TL-001");

        var service = CreateService(context);
        var sheet = (await service.GetQrLabelsAsync(seed.FarmId, new AnimalListFilter { Page = 0, PageSize = 5000 })).Value!;

        // An unbounded page is not a sheet: the ceiling is the list's, and a page below one is
        // page one rather than an empty sheet.
        Assert.Equal(200, sheet.PageSize);
        Assert.Equal(1, sheet.Page);
    }

    // ── The reader ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reader_RoundTripsEveryPayloadTheBuilderProduces()
    {
        var animalId = Guid.NewGuid();
        var payload = AnimalQrCode.BuildUrl("https://farm.example.com", animalId);

        Assert.True(AnimalQrCode.TryReadAnimalId(payload, out var read));

        Assert.Equal(animalId, read);
    }

    [Fact]
    public void RouteMarker_IsTheSpaRouteTheClientResolves()
    {
        // Deliberately a literal: the marker is mirrored by the client's parser, and a change
        // to one side without the other is a printed label that stops resolving.
        Assert.Equal("/dashboard/animals/", AnimalQrCode.RouteMarker);
    }

    [Theory]
    [InlineData("TL-001")]
    [InlineData("http://farm.example.com/dashboard")]                                    // the app's root, not an animal
    [InlineData("http://farm.example.com/dashboard/animals/")]                           // no id
    [InlineData("http://farm.example.com/dashboard/animals/not-a-guid")]                 // readable but unresolvable
    [InlineData("http://farm.example.com/dashboard/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff/weights")] // a different address
    [InlineData("http://farm.example.com/api/farm/1/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff")]        // the API, not the SPA
    [InlineData("")]
    [InlineData("   ")]
    public void Reader_RejectsAnythingThatIsNotOneAnimalsRoute(string scanned)
    {
        Assert.False(AnimalQrCode.TryReadAnimalId(scanned, out var read));
        Assert.Equal(Guid.Empty, read);
    }

    [Theory]
    [InlineData("HTTPS://FARM.EXAMPLE.COM/DASHBOARD/ANIMALS/6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [InlineData("  http://farm.example.com/dashboard/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff  ")]
    [InlineData("http://farm.example.com/dashboard/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff?utm_source=tag")]
    [InlineData("http://farm.example.com/dashboard/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff#notes")]
    public void Reader_AcceptsTheSameLabelWhenAReaderDecoratesIt(string scanned)
    {
        // A camera that upper-cases an URL, a scanner that appends a campaign parameter, or a
        // paste with whitespace is still the same label — refusing those would fail in the
        // field for reasons that have nothing to do with the animal.
        Assert.True(AnimalQrCode.TryReadAnimalId(scanned, out var read));

        Assert.Equal(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), read);
    }

    [Fact]
    public void Reader_IgnoresTheFrontendOriginSoARedeploymentDoesNotInvalidatePrintedTags()
    {
        var animalId = Guid.NewGuid();

        Assert.True(AnimalQrCode.TryReadAnimalId($"https://old.example.com/dashboard/animals/{animalId}", out var read));

        Assert.Equal(animalId, read);
    }
}
