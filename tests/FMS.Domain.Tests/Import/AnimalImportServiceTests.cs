using System.Text;
using FMS.Application.Animal;
using FMS.Application.Animal.Import;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Configuration;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The import pipeline: column mapping, lookup resolution, row validation and the
/// all-or-nothing commit.
///
/// The rules a row is checked against are the ones the create endpoint already
/// enforces — the same <c>CreateAnimalRequestValidator</c> and the same
/// <see cref="AnimalService"/> creation rules — so several tests here assert the two
/// paths answer identically rather than re-stating the expected message.
/// </summary>
public class AnimalImportServiceTests
{
    private const string FullHeaders =
        "tagNumber,name,animalType,breed,sex,status,location,ageCategory,dateOfBirth,acquisitionDate,sireTag,damTag,notes";

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "IMP-001,Bella,Cattle,Holstein,Female,Active,Main Barn,Adult,2023-04-15,2023-05-01,,,First\n" +
                  "IMP-002,,Cattle,,Male,Active,,,,,,,\n";

        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.True(preview.IsSuccess);
        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(2, dto.ValidRowCount);
        Assert.Equal(0, dto.InvalidRowCount);
        Assert.Empty(dto.InvalidRows);
        Assert.Equal(2, dto.SampleValidRows.Count);
        Assert.False(dto.Truncated);

        // A preview is read-only.
        Assert.Equal(0, await harness.Context.Animals.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFilesHeaders_TheSuggestedMapping_AndTheFarmsLookupOptions()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.Seed.FarmId, Utf8($"{FullHeaders}\nIMP-001,,,,,,,,,,,,\n"), "animals.csv", null);

        Assert.True(preview.IsSuccess);
        var dto = preview.Value!;

        Assert.Equal(FullHeaders.Split(','), dto.Headers);
        Assert.Equal(0, dto.SuggestedMapping.For(AnimalImportFields.TagNumber)!.Column);
        Assert.Equal(dto.SuggestedMapping.Fields.Count, dto.Mapping.Fields.Count);

        // The wizard's "fixed value" picker offers the farm's real lookups.
        Assert.Contains("Cattle", dto.Lookups[AnimalImportFields.AnimalType]);
        Assert.Contains("Female", dto.Lookups[AnimalImportFields.Sex]);
        Assert.Contains("Active", dto.Lookups[AnimalImportFields.Status]);
        Assert.Contains("Main Barn", dto.Lookups[AnimalImportFields.Location]);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryAnimal_WithItsRegisteredTimelineEvent()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "IMP-001,Bella,Cattle,Holstein,Female,Active,Main Barn,Adult,2023-04-15,2023-05-01,,,First\n" +
                  "IMP-002,,Cattle,,Male,Active,,,,,,,\n";

        var commit = await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(2, commit.Value!.ImportedCount);
        Assert.Equal(2, commit.Value.TotalRows);
        Assert.Empty(commit.Value.InvalidRows);

        var animals = await harness.Context.Animals
            .Include(animal => animal.TimelineEvents)
            .OrderBy(animal => animal.TagNumber)
            .ToListAsync();

        Assert.Equal(new[] { "IMP-001", "IMP-002" }, animals.Select(animal => animal.TagNumber));

        var first = animals[0];
        Assert.Equal(harness.Seed.AnimalTypeId, first.AnimalTypeId);
        Assert.Equal(harness.Seed.BreedId, first.BreedId);
        Assert.Equal(harness.Seed.SexFemaleId, first.SexOptionId);
        Assert.Equal(harness.Seed.StatusActiveId, first.AnimalStatusId);
        Assert.Equal(harness.Seed.LocationId, first.LocationId);
        Assert.Equal(harness.Seed.AgeCategoryId, first.AgeCategoryId);
        Assert.Equal(new DateTime(2023, 4, 15, 0, 0, 0, DateTimeKind.Utc), first.DateOfBirth);
        Assert.Equal(new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc), first.AcquisitionDate);
        Assert.Equal("First", first.Notes);

        // An imported animal carries the same history a hand-entered one does.
        var timeline = Assert.Single(first.TimelineEvents);
        Assert.Equal(TimelineEventTypes.Created, timeline.EventType);
        Assert.Equal("Animal registered", timeline.Title);
        Assert.Equal(harness.Seed.FarmId, timeline.FarmId);

        Assert.True(harness.Logs.HasMessageContaining("Imported 2 animals"));
    }

    [Fact]
    public async Task Preview_AFixedValueMapping_FillsColumnsTheFileDoesNotHave()
    {
        using var harness = await CreateHarnessAsync();

        var mapping = new AnimalImportMapping
        {
            Fields = new Dictionary<string, AnimalImportFieldMap>
            {
                [AnimalImportFields.TagNumber] = new() { Column = 0 },
                [AnimalImportFields.AnimalType] = new() { Column = 1 },
                // Case-insensitive, because that is how a spreadsheet's free text arrives.
                [AnimalImportFields.Sex] = new() { Constant = "female" },
                [AnimalImportFields.Status] = new() { Constant = "ACTIVE" },
            }
        };

        var csv = "tagNumber,animalType\nIMP-101,Cattle\nIMP-102,Cattle\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", mapping);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);
        Assert.Equal("female", preview.Value.SampleValidRows[0].Values[AnimalImportFields.Sex]);

        var commit = await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", mapping);

        Assert.Equal(2, commit.Value!.ImportedCount);
        var imported = await harness.Context.Animals.ToListAsync();
        Assert.All(imported, animal => Assert.Equal(harness.Seed.SexFemaleId, animal.SexOptionId));
        Assert.All(imported, animal => Assert.Equal(harness.Seed.StatusActiveId, animal.AnimalStatusId));
    }

    [Fact]
    public async Task Commit_MixedRows_WritesNothingAtAll_AndReportsEveryInvalidRow()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status\n" +
                  "MIX-001,Cattle,Female,Active\n" +
                  "MIX-002,Unicorns,Female,Active\n" +
                  "MIX-003,Cattle,Robot,Active\n" +
                  "MIX-004,Cattle,Female,Active\n";

        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);
        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.ValidRowCount);
        Assert.Equal(2, preview.Value.InvalidRowCount);
        Assert.Equal(new[] { 3, 4 }, preview.Value.InvalidRows.Select(row => row.RowNumber));

        var commit = await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(4, commit.Value!.TotalRows);
        Assert.Equal(0, commit.Value.ImportedCount);
        Assert.Equal(new[] { 3, 4 }, commit.Value.InvalidRows.Select(row => row.RowNumber));

        // All-or-nothing: the two good rows of the file were not written either.
        Assert.Empty(await harness.Context.Animals.ToListAsync());

        // And the refusal is visible, not silent.
        Assert.True(harness.Logs.HasMessageContaining("Refused animal import"));
        Assert.True(harness.Logs.HasMessageContaining("2 of 4 rows"));
    }

    [Fact]
    public async Task Preview_DuplicateTag_ReportsExactlyWhatTheCreateEndpointReports()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingAnimalAsync(harness, "DUP-001");

        var csv = "tagNumber,animalType,sex,status\nDUP-001,Cattle,Female,Active\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.True(preview.IsSuccess);
        var row = Assert.Single(preview.Value!.InvalidRows);
        var error = Assert.Single(row.Errors);
        Assert.Equal(AnimalImportFields.TagNumber, error.Field);

        // Duplicates are an error, never a silent skip and never an update: the same
        // request through the create endpoint has to say the same thing.
        var endpoint = await harness.Animals.CreateAnimalAsync(harness.Seed.FarmId, new CreateAnimalRequest
        {
            TagNumber = "DUP-001",
            AnimalTypeId = harness.Seed.AnimalTypeId,
            SexOptionId = harness.Seed.SexFemaleId,
            AnimalStatusId = harness.Seed.StatusActiveId,
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal("Conflict", endpoint.Error!.Code);
        Assert.Equal(endpoint.Error.Message, error.Message);
    }

    [Fact]
    public async Task Preview_ASoftDeletedAnimalWithTheSameTag_IsNotAConflict()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingAnimalAsync(harness, "GONE-001", isDeleted: true);

        var csv = "tagNumber,animalType,sex,status\nGONE-001,Cattle,Female,Active\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        // Parity with the create endpoint, which only considers live animals.
        Assert.Equal(1, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);
    }

    [Fact]
    public async Task Preview_DuplicateTagWithinTheFile_FlagsTheLaterRow_AndNamesTheEarlierOne()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status\n" +
                  "ROW-001,Cattle,Female,Active\n" +
                  "ROW-002,Cattle,Female,Active\n" +
                  "ROW-001,Cattle,Female,Active\n";

        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.Equal(2, preview.Value!.ValidRowCount);
        var row = Assert.Single(preview.Value.InvalidRows);
        Assert.Equal(4, row.RowNumber);
        Assert.Contains("row 2", Assert.Single(row.Errors).Message);
    }

    [Fact]
    public async Task Preview_UnknownLookupNames_AreEachReportedAgainstTheirField()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status,location\n" +
                  "UNK-001,Unicorns,Robot,Zombie,Mars\n";

        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var errors = Assert.Single(preview.Value!.InvalidRows).Errors;
        Assert.Equal(4, errors.Count);
        Assert.Equal(AnimalImportFields.AnimalType, errors[0].Field);
        Assert.Contains("Unicorns", errors[0].Message);
        Assert.Contains("Robot", errors[1].Message);
        Assert.Contains("Zombie", errors[2].Message);
        Assert.Contains("Mars", errors[3].Message);
    }

    [Fact]
    public async Task Preview_ABreedFromAnotherAnimalType_IsARowErrorThatNamesBoth()
    {
        using var harness = await CreateHarnessAsync();
        var goat = new AnimalType { Id = Guid.NewGuid(), FarmId = harness.Seed.FarmId, Name = "Goat" };
        harness.Context.AnimalTypes.Add(goat);
        harness.Context.Breeds.Add(new Breed
        {
            Id = Guid.NewGuid(),
            Name = "Boer",
            AnimalTypeId = goat.Id,
            AnimalType = goat,
            AverageGestationDays = 150
        });
        await harness.Context.SaveChangesAsync();

        var csv = "tagNumber,animalType,breed,sex,status\nBRD-001,Cattle,Boer,Female,Active\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.Breed, error.Field);
        Assert.Contains("Boer", error.Message);
        Assert.Contains("Cattle", error.Message);
    }

    [Fact]
    public async Task Preview_FutureDateOfBirth_IsRejectedByTheSharedValidator()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status,dateOfBirth\nFUT-001,Cattle,Female,Active,2999-01-01\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.DateOfBirth, error.Field);
        Assert.Equal("Date of birth cannot be in the future", error.Message);
    }

    [Fact]
    public async Task Preview_TagBeyondTheValidatorsLength_IsRejected()
    {
        using var harness = await CreateHarnessAsync();
        var longTag = new string('T', 51);

        var csv = $"tagNumber,animalType,sex,status\n{longTag},Cattle,Female,Active\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        // The row is checked by the same CreateAnimalRequest validator the create
        // endpoint registers, so its limits apply to imports too.
        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.TagNumber, error.Field);
        Assert.Equal("Tag number cannot exceed 50 characters", error.Message);
    }

    [Fact]
    public async Task Preview_AnAmbiguousDate_IsReportedRatherThanGuessed()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status,dateOfBirth\nAMB-001,Cattle,Female,Active,01/02/2023\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.DateOfBirth, error.Field);
        Assert.Contains("ambiguous", error.Message);

        // And the caller resolves it explicitly instead of the import picking one.
        var resolved = await harness.Service.PreviewAsync(
            harness.Seed.FarmId, Utf8(csv), "animals.csv", new AnimalImportMapping { DateFormat = "dd/MM/yyyy" });

        Assert.Equal(0, resolved.Value!.InvalidRowCount);
    }

    [Fact]
    public async Task Preview_AnUnambiguousSlashedDate_IsAccepted()
    {
        using var harness = await CreateHarnessAsync();

        // 25 cannot be a month, so this can only be 25 April — no guessing involved.
        var csv = "tagNumber,animalType,sex,status,dateOfBirth\nSLD-001,Cattle,Female,Active,25/04/2023\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.Equal(0, preview.Value!.InvalidRowCount);

        await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);
        var animal = await harness.Context.Animals.SingleAsync();
        Assert.Equal(new DateTime(2023, 4, 25, 0, 0, 0, DateTimeKind.Utc), animal.DateOfBirth);
    }

    [Fact]
    public async Task Preview_AMappingWithNoRequiredField_IsRejectedBeforeAnyRowIsRead()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.Seed.FarmId, Utf8("name\nBella\n"), "animals.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Equal("Validation", preview.Error!.Code);
        Assert.Contains("Tag number", preview.Error.Message);
    }

    [Fact]
    public async Task Preview_AMappingPointingPastTheFilesColumns_IsRejected()
    {
        using var harness = await CreateHarnessAsync();

        var mapping = new AnimalImportMapping
        {
            Fields = new Dictionary<string, AnimalImportFieldMap>
            {
                [AnimalImportFields.TagNumber] = new() { Column = 9 },
                [AnimalImportFields.AnimalType] = new() { Constant = "Cattle" },
                [AnimalImportFields.Sex] = new() { Constant = "Female" },
                [AnimalImportFields.Status] = new() { Constant = "Active" },
            }
        };

        var preview = await harness.Service.PreviewAsync(
            harness.Seed.FarmId, Utf8("tagNumber\nMAP-001\n"), "animals.csv", mapping);

        Assert.False(preview.IsSuccess);
        Assert.Equal("Validation", preview.Error!.Code);
    }

    [Fact]
    public async Task Preview_ANonCsvFormatThatCannotBeParsed_IsAValidationError()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.Seed.FarmId, Utf8("not a spreadsheet"), "animals.xls", null);

        Assert.False(preview.IsSuccess);
        Assert.Equal("Validation", preview.Error!.Code);
    }

    [Fact]
    public async Task Preview_MoreRowsThanAllowed_IsRejected()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxRows = 2);

        var csv = "tagNumber,animalType,sex,status\nA-1,Cattle,Female,Active\nA-2,Cattle,Female,Active\nA-3,Cattle,Female,Active\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("more than 2 rows", preview.Error!.Message);
    }

    [Fact]
    public async Task Preview_ProblemRowsPastTheReportCap_AreCountedButTruncated()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxReportedRows = 1);

        var csv = "tagNumber,animalType,sex,status\n" +
                  "CAP-001,Unicorns,Female,Active\n" +
                  "CAP-002,Unicorns,Female,Active\n";

        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.Equal(2, preview.Value!.InvalidRowCount);
        Assert.Single(preview.Value.InvalidRows);
        Assert.True(preview.Value.Truncated);
    }

    [Fact]
    public async Task Commit_CalfReferencingItsDamEarlierInTheSameFile_ResolvesTheDam()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status,damTag,sireTag\n" +
                  "DAM-001,Cattle,Female,Active,,\n" +
                  "SIRE-001,Cattle,Male,Active,,\n" +
                  "CALF-001,Cattle,Female,Active,DAM-001,SIRE-001\n";

        var commit = await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        Assert.Equal(3, commit.Value!.ImportedCount);

        var dam = await harness.Context.Animals.SingleAsync(animal => animal.TagNumber == "DAM-001");
        var sire = await harness.Context.Animals.SingleAsync(animal => animal.TagNumber == "SIRE-001");
        var calf = await harness.Context.Animals.SingleAsync(animal => animal.TagNumber == "CALF-001");

        Assert.Equal(dam.Id, calf.DamId);
        Assert.Equal(sire.Id, calf.SireId);
    }

    [Fact]
    public async Task Preview_AParentTagThatExistsButNotInThisFarm_IsNotResolved()
    {
        using var harness = await CreateHarnessAsync();

        // The same tag exists in a different farm; farm isolation must keep it out.
        var otherFarm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Other" };
        harness.Context.Farms.Add(otherFarm);
        await SeedExistingAnimalAsync(harness, "ALIEN-001", otherFarm.Id);

        var csv = "tagNumber,animalType,sex,status,damTag\nISO-001,Cattle,Female,Active,ALIEN-001\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.DamTag, error.Field);
    }

    [Fact]
    public async Task Preview_ARowReferencingItsOwnTagAsParent_IsRejected()
    {
        using var harness = await CreateHarnessAsync();

        var csv = "tagNumber,animalType,sex,status,damTag\nSELF-001,Cattle,Female,Active,SELF-001\n";
        var preview = await harness.Service.PreviewAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(AnimalImportFields.DamTag, error.Field);
        Assert.Contains("own tag", error.Message);
    }

    [Fact]
    public async Task Preview_AMappedFieldMayBeExplicitlyIgnored_EvenWhenTheHeaderMatches()
    {
        using var harness = await CreateHarnessAsync();

        var mapping = new AnimalImportMapping
        {
            Fields = new Dictionary<string, AnimalImportFieldMap>
            {
                [AnimalImportFields.TagNumber] = new() { Column = 0 },
                [AnimalImportFields.AnimalType] = new() { Column = 1 },
                [AnimalImportFields.Sex] = new() { Constant = "Female" },
                [AnimalImportFields.Status] = new() { Constant = "Active" },
                // Auto-detection would map "name"; the caller says no.
                [AnimalImportFields.Name] = new(),
            }
        };

        var csv = "tagNumber,animalType,name\nIGN-001,Cattle,Bella\n";
        var commit = await harness.Service.CommitAsync(harness.Seed.FarmId, Utf8(csv), "animals.csv", mapping);

        Assert.Equal(1, commit.Value!.ImportedCount);
        var animal = await harness.Context.Animals.SingleAsync();
        Assert.Null(animal.Name);
    }

    // ── Harness ────────────────────────────────────────────────────────────

    internal sealed record Seed(
        Guid FarmId,
        Guid AnimalTypeId,
        Guid BreedId,
        Guid SexFemaleId,
        Guid SexMaleId,
        Guid StatusActiveId,
        Guid LocationId,
        Guid AgeCategoryId);

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Seed Seed { get; init; }
        public required AnimalImportService Service { get; init; }
        public required IAnimalService Animals { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }

        public void Dispose() => Context.Dispose();
    }

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<Harness> CreateHarnessAsync(Action<AnimalImportOptions>? configure = null)
    {
        var context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var seed = await SeedFarmAsync(context);
        var (logger, logs) = TestLoggers.Create<AnimalImportService>();
        var currentUser = new FixedCurrentUser();
        var animals = new AnimalService(
            context, currentUser, new FileStorageService(Path.Combine(Path.GetTempPath(), "fms-import-tests")));

        var options = new AnimalImportOptions();
        configure?.Invoke(options);

        var service = new AnimalImportService(
            new SpreadsheetReader(),
            new ConfigurationService(context),
            animals,
            context,
            new FMS.API.Validation.Animals.CreateAnimalRequestValidator(),
            Options.Create(options),
            logger);

        return new Harness
        {
            Context = context,
            Seed = seed,
            Service = service,
            Animals = animals,
            Logs = logs
        };
    }

    private static async Task<Seed> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Import Farm" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            Name = "Holstein",
            AnimalTypeId = animalType.Id,
            AnimalType = animalType,
            AverageGestationDays = 283
        };

        // Lookups are farm-scoped, and locations carry a location type because the
        // configuration lookup navigates to it.
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Shed" };
        var female = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var male = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Male" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Active",
            Category = FMS.Domain.Enums.AnimalStatusCategory.Active,
            IsSystemDefined = true
        };
        var location = new Location
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Main Barn",
            LocationTypeId = locationType.Id,
            LocationType = locationType
        };
        var ageCategory = new AgeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Adult",
            MinDays = 365,
            MaxDays = 9999
        };

        context.AddRange(farm, animalType, breed, locationType, female, male, status, location, ageCategory);
        await context.SaveChangesAsync();

        return new Seed(
            farm.Id, animalType.Id, breed.Id, female.Id, male.Id, status.Id, location.Id, ageCategory.Id);
    }

    private static async Task SeedExistingAnimalAsync(
        Harness harness, string tagNumber, Guid? farmId = null, bool isDeleted = false)
    {
        harness.Context.Animals.Add(new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farmId ?? harness.Seed.FarmId,
            TagNumber = tagNumber,
            AnimalTypeId = harness.Seed.AnimalTypeId,
            SexOptionId = harness.Seed.SexFemaleId,
            AnimalStatusId = harness.Seed.StatusActiveId,
            IsDeleted = isDeleted
        });

        await harness.Context.SaveChangesAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
