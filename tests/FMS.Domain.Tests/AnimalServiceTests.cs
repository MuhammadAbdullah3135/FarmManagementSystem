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
        new(context, new FixedCurrentUser(), CreateFileStorage());

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
