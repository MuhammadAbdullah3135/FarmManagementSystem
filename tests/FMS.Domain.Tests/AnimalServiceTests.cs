using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class AnimalServiceTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static AnimalService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser(), CreateFileStorage(), TestConfiguration.WithFrontend());

    private static FMS.Infrastructure.Files.FileStorageService CreateFileStorage() =>
        new(Path.Combine(Path.GetTempPath(), "fms-test-uploads"));

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private static async Task<Farm> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Test Farm" };
        context.Farms.Add(farm);
        await context.SaveChangesAsync();
        return farm;
    }

    private static async Task<(Guid typeId, Guid breedId, Guid sexId, Guid activeStatusId, Guid soldStatusId, Guid locationId)> SeedLookupsAsync(FmsDbContext context, Farm farm)
    {
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var activeStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var soldStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sold", Category = AnimalStatusCategory.Terminal };
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn 1", LocationTypeId = locationType.Id };

        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.AddRange(activeStatus, soldStatus);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        return (type.Id, breed.Id, sex.Id, activeStatus.Id, soldStatus.Id, location.Id);
    }

    private static CreateAnimalRequest BuildCreateRequest(
        Guid typeId, Guid breedId, Guid sexId, Guid statusId, Guid? locationId, string tag = "TAG-001") =>
        new()
        {
            TagNumber = tag,
            Name = "Bessie",
            AnimalTypeId = typeId,
            BreedId = breedId,
            SexOptionId = sexId,
            AnimalStatusId = statusId,
            LocationId = locationId,
            DateOfBirth = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        };

    [Fact]
    public async Task CreateAnimal_WithValidData_Succeeds_AndWritesCreatedTimelineEvent()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);
        var service = CreateService(context);

        var result = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, lookups.locationId));

        Assert.True(result.IsSuccess);
        Assert.Equal("TAG-001", result.Value!.TagNumber);
        Assert.Equal("Cow", result.Value.AnimalTypeName);
        Assert.Equal("Holstein", result.Value.BreedName);

        var events = await context.AnimalTimelineEvents
            .Where(e => e.AnimalId == result.Value.Id)
            .ToListAsync();
        var createdEvent = Assert.Single(events);
        Assert.Equal(TimelineEventTypes.Created, createdEvent.EventType);
    }

    [Fact]
    public async Task CreateAnimal_DuplicateTagInFarm_ReturnsConflict()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);
        var service = CreateService(context);

        await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));

        var second = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));

        Assert.False(second.IsSuccess);
        Assert.Equal("Conflict", second.Error!.Code);
    }

    [Fact]
    public async Task CreateAnimal_BreedFromDifferentAnimalType_ReturnsValidation()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);

        var otherType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sheep" };
        context.AnimalTypes.Add(otherType);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var request = BuildCreateRequest(otherType.Id, lookups.breedId, lookups.sexId, lookups.activeStatusId, null);

        var result = await service.CreateAnimalAsync(farm.Id, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task ChangeStatus_UpdatesStatus_AndWritesTimelineEvent()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);
        var service = CreateService(context);

        var created = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));

        var result = await service.ChangeStatusAsync(farm.Id, created.Value!.Id,
            new ChangeAnimalStatusRequest { NewStatusId = lookups.soldStatusId, Reason = "Sold at market" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Sold", result.Value!.StatusName);
        Assert.Equal(AnimalStatusCategory.Terminal, result.Value.StatusCategory);

        var statusEvents = await context.AnimalTimelineEvents
            .Where(e => e.AnimalId == created.Value.Id && e.EventType == TimelineEventTypes.StatusChanged)
            .ToListAsync();
        var statusEvent = Assert.Single(statusEvents);
        Assert.Contains("Sold", statusEvent.Title);
        Assert.Equal("Sold at market", statusEvent.Description);
    }

    [Fact]
    public async Task GetAnimals_ExcludesTerminalStatus_ByDefault_AndIncludesWhenRequested()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);
        var service = CreateService(context);

        var active = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null, "TAG-A"));
        var sold = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.soldStatusId, null, "TAG-S"));

        var defaultList = await service.GetAnimalsAsync(farm.Id, new AnimalListFilter());
        var fullList = await service.GetAnimalsAsync(farm.Id, new AnimalListFilter { IncludeTerminal = true });

        Assert.True(defaultList.IsSuccess);
        Assert.Single(defaultList.Value!.Items);
        Assert.Equal("TAG-A", defaultList.Value.Items[0].TagNumber);

        Assert.Equal(2, fullList.Value!.TotalCount);
    }

    [Fact]
    public async Task DeleteAnimal_SoftDeletes_AndReleasesTagForReuse()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);
        var service = CreateService(context);

        var created = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));

        var deleteResult = await service.DeleteAnimalAsync(farm.Id, created.Value!.Id);
        Assert.True(deleteResult.IsSuccess);

        var getById = await service.GetAnimalByIdAsync(farm.Id, created.Value.Id);
        Assert.False(getById.IsSuccess);
        Assert.Equal("NotFound", getById.Error!.Code);

        var recreate = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));
        Assert.True(recreate.IsSuccess);
    }

    // ── Parentage ────────────────────────────────────────────────────────────────────────
    //
    // A sire has to be male and a dam female, and neither may be the animal itself. The rules
    // are gated on the request *changing* that parent, because the farm already holds one
    // impossible pairing (a female recorded as the sire of TAG-0079 Boocho) and the users who
    // have to correct that record are exactly the users the rule must not lock out.

    /// <summary>
    /// A farm with both sexes recorded and three animals: a bull, a cow, and a calf. The rules
    /// need all three — "the parent has to be the right sex" and "the parent cannot be the
    /// animal itself" are both answered against stored animals.
    /// </summary>
    private sealed record ParentageFarm(
        Farm Farm,
        Guid AnimalTypeId,
        Guid BreedId,
        Guid ActiveStatusId,
        Guid MaleSexId,
        Guid FemaleSexId,
        Animal Bull,
        Animal Cow,
        Animal Calf);

    private static async Task<ParentageFarm> SeedParentageAsync(FmsDbContext context)
    {
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);

        // The seeded pair is Female alone; the male option is what makes a valid sire possible.
        var maleSex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Male" };
        context.SexOptions.Add(maleSex);

        Animal AnimalOf(string tag, Guid sexId, Guid? sireId = null, Guid? damId = null)
        {
            var animal = new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                TagNumber = tag,
                AnimalTypeId = lookups.typeId,
                BreedId = lookups.breedId,
                SexOptionId = sexId,
                AnimalStatusId = lookups.activeStatusId,
                SireId = sireId,
                DamId = damId
            };
            context.Animals.Add(animal);
            return animal;
        }

        var bull = AnimalOf("BULL-001", maleSex.Id);
        var cow = AnimalOf("COW-001", lookups.sexId);
        var calf = AnimalOf("CALF-001", lookups.sexId);
        await context.SaveChangesAsync();

        return new ParentageFarm(
            farm, lookups.typeId, lookups.breedId, lookups.activeStatusId,
            maleSex.Id, lookups.sexId, bull, cow, calf);
    }

    private static UpdateAnimalRequest BuildUpdateRequest(
        Animal animal, Guid? sireId = null, Guid? damId = null, string? name = null) => new()
        {
            TagNumber = animal.TagNumber,
            Name = name ?? animal.Name,
            BreedId = animal.BreedId,
            SexOptionId = animal.SexOptionId,
            AgeCategoryId = animal.AgeCategoryId,
            LocationId = animal.LocationId,
            SireId = sireId,
            DamId = damId,
            DateOfBirth = animal.DateOfBirth,
            AcquisitionDate = animal.AcquisitionDate,
            Notes = animal.Notes
        };

    [Fact]
    public async Task CreateAnimal_WithAFemaleSire_IsRejectedWithAKeyedMessage()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.FemaleSexId, seed.ActiveStatusId, null, "CALF-002");
        request.SireId = seed.Cow.Id;

        var result = await service.CreateAnimalAsync(seed.Farm.Id, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Equal("The sire must be male", result.Error.Message);
        Assert.Equal("validation.animal.sireMustBeMale", result.Error.MessageKey);
    }

    [Fact]
    public async Task CreateAnimal_WithAMaleDam_IsRejectedWithAKeyedMessage()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.MaleSexId, seed.ActiveStatusId, null, "CALF-003");
        request.DamId = seed.Bull.Id;

        var result = await service.CreateAnimalAsync(seed.Farm.Id, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Equal("The dam must be female", result.Error.Message);
        Assert.Equal("validation.animal.damMustBeFemale", result.Error.MessageKey);
    }

    [Fact]
    public async Task CreateAnimal_WithAMaleSireAndAFemaleDam_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.FemaleSexId, seed.ActiveStatusId, null, "CALF-004");
        request.SireId = seed.Bull.Id;
        request.DamId = seed.Cow.Id;

        var result = await service.CreateAnimalAsync(seed.Farm.Id, request);

        Assert.True(result.IsSuccess);
        var stored = await context.Animals.FindAsync(result.Value!.Id);
        Assert.Equal(seed.Bull.Id, stored!.SireId);
        Assert.Equal(seed.Cow.Id, stored.DamId);
    }

    [Fact]
    public async Task CreateAnimal_WithAParentWhoseSexIsAnotherWord_IsAllowed()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        // A farm that named its sexes something else entirely: the rule cannot read the word,
        // so it stays out of the way instead of refusing every parent that farm records.
        var renamed = new SexOption { Id = Guid.NewGuid(), FarmId = seed.Farm.Id, Value = "Buck" };
        context.SexOptions.Add(renamed);
        var buck = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = seed.Farm.Id,
            TagNumber = "BUCK-001",
            AnimalTypeId = seed.AnimalTypeId,
            SexOptionId = renamed.Id,
            AnimalStatusId = seed.ActiveStatusId
        };
        context.Animals.Add(buck);
        await context.SaveChangesAsync();

        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.FemaleSexId, seed.ActiveStatusId, null, "CALF-005");
        request.SireId = buck.Id;

        Assert.True((await service.CreateAnimalAsync(seed.Farm.Id, request)).IsSuccess);
    }

    [Fact]
    public async Task CreateAnimals_NamingItselfAsSire_IsRejected()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        // The single-row path generates the id, so self-reference is exercised where a caller
        // supplies ids: the batch path a bulk import uses.
        var ownId = Guid.NewGuid();
        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.FemaleSexId, seed.ActiveStatusId, null, "LOOP-001");
        request.SireId = ownId;

        var result = await service.CreateAnimalsAsync(
            seed.Farm.Id, new[] { new BulkAnimalCreateItem { Id = ownId, Request = request } });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.SuccessCount);
        Assert.Equal("Sire animal not found", Assert.Single(result.Value.Failures).Message);
    }

    [Fact]
    public async Task CreateAnimals_WithAFemaleSire_IsRejected()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        // The batch path shares the rule with a single create, so an imported row cannot name a
        // parent the endpoint refuses.
        var request = BuildCreateRequest(
            seed.AnimalTypeId, seed.BreedId, seed.FemaleSexId, seed.ActiveStatusId, null, "CALF-006");
        request.SireId = seed.Cow.Id;

        var result = await service.CreateAnimalsAsync(
            seed.Farm.Id, new[] { new BulkAnimalCreateItem { Id = Guid.NewGuid(), Request = request } });

        Assert.Equal(0, result.Value!.SuccessCount);
        Assert.Equal("The sire must be male", Assert.Single(result.Value.Failures).Message);
    }

    [Fact]
    public async Task UpdateAnimal_ChangingTheSireToAFemale_IsRejectedWithAKeyedMessage()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, sireId: seed.Cow.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Equal("validation.animal.sireMustBeMale", result.Error.MessageKey);
    }

    [Fact]
    public async Task UpdateAnimal_ChangingTheDamToAMale_IsRejectedWithAKeyedMessage()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, damId: seed.Bull.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("validation.animal.damMustBeFemale", result.Error!.MessageKey);
    }

    [Fact]
    public async Task UpdateAnimal_NamingItselfAsSire_IsRejected()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, sireId: seed.Calf.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
        Assert.Equal("Sire animal not found", result.Error.Message);
        Assert.Equal("validation.animal.sireNotFound", result.Error.MessageKey);
    }

    [Fact]
    public async Task UpdateAnimal_LeavingAnImpossibleParentInPlace_KeepsTheRecordEditable()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);

        // The farm's real record: a female stored as the sire. It was saved before the rule
        // existed, and the user correcting it must be able to fix one field at a time.
        seed.Calf.SireId = seed.Cow.Id;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        // Re-sending the stored value is not a change.
        var unchanged = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, sireId: seed.Cow.Id));
        Assert.True(unchanged.IsSuccess);

        // The edit form sends the whole record back, so the impossible parent travels with it
        // while the user corrects the name: that must save.
        var corrected = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, sireId: seed.Cow.Id, name: "Boocho"));
        Assert.True(corrected.IsSuccess);
        Assert.Equal("Boocho", (await context.Animals.FindAsync(seed.Calf.Id))!.Name);
        Assert.Equal(seed.Cow.Id, (await context.Animals.FindAsync(seed.Calf.Id))!.SireId);

        // Clearing the mistake is the actual fix, and the rule has nothing to say about absence.
        var cleared = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, name: "Boocho"));
        Assert.True(cleared.IsSuccess);
        Assert.Null((await context.Animals.FindAsync(seed.Calf.Id))!.SireId);
    }

    [Fact]
    public async Task UpdateAnimal_ReplacingAnImpossibleParentWithAnother_IsRejected()
    {
        using var context = CreateContext();
        var seed = await SeedParentageAsync(context);
        seed.Calf.SireId = seed.Cow.Id;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        // Clearing the female sire is allowed, but nothing may take its place on that side of
        // the pedigree except a male animal.
        var result = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, sireId: seed.Bull.Id));
        Assert.True(result.IsSuccess);

        var rejected = await service.UpdateAnimalAsync(
            seed.Farm.Id, seed.Calf.Id, BuildUpdateRequest(seed.Calf, damId: seed.Bull.Id));
        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation.animal.damMustBeFemale", rejected.Error!.MessageKey);
    }

    [Fact]
    public async Task AddIdentification_DuplicateActiveValue_ReturnsConflict()
    {
        using var context = CreateContext();
        var farm = await SeedFarmAsync(context);
        var lookups = await SeedLookupsAsync(context, farm);

        var idType = new IdentificationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Ear Tag" };
        context.IdentificationTypes.Add(idType);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var created = await service.CreateAnimalAsync(farm.Id,
            BuildCreateRequest(lookups.typeId, lookups.breedId, lookups.sexId, lookups.activeStatusId, null));

        var first = await service.AddIdentificationAsync(farm.Id, created.Value!.Id,
            new AddAnimalIdentificationRequest { IdentificationTypeId = idType.Id, Value = "RFID-123" });
        Assert.True(first.IsSuccess);

        var duplicate = await service.AddIdentificationAsync(farm.Id, created.Value.Id,
            new AddAnimalIdentificationRequest { IdentificationTypeId = idType.Id, Value = "RFID-123" });

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);
    }
}
