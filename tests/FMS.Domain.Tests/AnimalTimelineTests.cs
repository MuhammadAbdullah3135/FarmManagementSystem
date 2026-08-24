using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class AnimalTimelineTests
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

    private static async Task<(Guid animalId, Guid activeStatusId, Guid soldStatusId)> SeedAnimalAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var activeStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var soldStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sold", Category = AnimalStatusCategory.Terminal };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.AddRange(activeStatus, soldStatus);

        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TL-001",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = activeStatus.Id,
            DateOfBirth = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Animals.Add(animal);
        context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farm.Id,
            EventType = TimelineEventTypes.Created,
            Title = "Animal registered",
            OccurredAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return (animal.Id, activeStatus.Id, soldStatus.Id);
    }

    [Fact]
    public async Task GetTimeline_ReturnsEventsNewestFirst()
    {
        using var context = CreateContext();
        var (animalId, activeStatusId, soldStatusId) = await SeedAnimalAsync(context);
        var farmId = (await context.Animals.AsNoTracking().FirstAsync(a => a.Id == animalId)).FarmId;
        var service = CreateService(context);

        await service.ChangeStatusAsync(farmId, animalId,
            new ChangeAnimalStatusRequest { NewStatusId = soldStatusId });

        var result = await service.GetTimelineAsync(farmId, animalId, page: 1, pageSize: 20, eventType: null);

        Assert.True(result.IsSuccess);
        var paged = result.Value!;
        Assert.Equal(2, paged.TotalCount);

        Assert.Equal(TimelineEventTypes.StatusChanged, paged.Items[0].EventType);
        Assert.Equal(TimelineEventTypes.Created, paged.Items[1].EventType);
    }

    [Fact]
    public async Task GetTimeline_WithEventTypeFilter_ReturnsOnlyMatchingEvents()
    {
        using var context = CreateContext();
        var (animalId, _, _) = await SeedAnimalAsync(context);
        var farmId = (await context.Animals.AsNoTracking().FirstAsync(a => a.Id == animalId)).FarmId;
        var service = CreateService(context);

        var result = await service.GetTimelineAsync(farmId, animalId, page: 1, pageSize: 20, eventType: TimelineEventTypes.Created);

        Assert.True(result.IsSuccess);
        var paged = result.Value!;
        Assert.Equal(1, paged.TotalCount);
        Assert.All(paged.Items, e => Assert.Equal(TimelineEventTypes.Created, e.EventType));
    }

    [Fact]
    public async Task GetTimeline_AnimalNotFound_ReturnsNotFound()
    {
        using var context = CreateContext();
        var (animalId, _, _) = await SeedAnimalAsync(context);
        var farmId = (await context.Animals.AsNoTracking().FirstAsync(a => a.Id == animalId)).FarmId;
        var service = CreateService(context);

        var result = await service.GetTimelineAsync(farmId, Guid.NewGuid(), page: 1, pageSize: 20, eventType: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task GetTimeline_Pagination_ReturnsCorrectPage()
    {
        using var context = CreateContext();
        var (animalId, _, _) = await SeedAnimalAsync(context);
        var farmId = (await context.Animals.AsNoTracking().FirstAsync(a => a.Id == animalId)).FarmId;
        var service = CreateService(context);

        for (var i = 1; i <= 5; i++)
        {
            context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = animalId,
                FarmId = farmId,
                EventType = TimelineEventTypes.WeightRecorded,
                Title = $"Event {i}",
                OccurredAt = new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var page2 = await service.GetTimelineAsync(farmId, animalId, page: 2, pageSize: 3, eventType: null);

        Assert.True(page2.IsSuccess);
        var paged = page2.Value!;
        Assert.Equal(6, paged.TotalCount);
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal("Event 2", paged.Items[0].Title);
        Assert.Equal("Event 1", paged.Items[1].Title);
        Assert.Equal(TimelineEventTypes.Created, paged.Items[2].EventType);
    }
}
