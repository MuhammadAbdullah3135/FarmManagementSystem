using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// A status change records which status it moved to, in the event's related id.
///
/// This is what lets the cost report tell "the animal left" from "the animal changed pen"
/// without parsing the event's title — and a title-parsing fallback exists for rows written
/// before it, but it can miss when a status is renamed. So the id is the fix, not the
/// fallback, and these tests hold both status-change paths to it.
/// </summary>
public class AnimalStatusDepartureIdTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AnimalService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser(),
            new FileStorageService(Path.Combine(Path.GetTempPath(), "fms-test-uploads")));

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record Seed(Guid FarmId, Guid ActiveStatusId, Guid SoldStatusId, Guid AnimalId);

    private static async Task<Seed> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var active = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active
        };
        var sold = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sold", Category = AnimalStatusCategory.Terminal
        };
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "ID-001",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = active.Id,
            AcquisitionDate = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.AddRange(active, sold);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, active.Id, sold.Id, animal.Id);
    }

    [Fact]
    public async Task ChangeStatus_RecordsTheStatusMovedTo_Single()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);

        var result = await CreateService(context).ChangeStatusAsync(
            seed.FarmId, seed.AnimalId, new ChangeAnimalStatusRequest
            {
                NewStatusId = seed.SoldStatusId,
                Reason = "Sold at market"
            });

        Assert.True(result.IsSuccess, result.Error?.Message);

        var evt = await context.AnimalTimelineEvents
            .SingleAsync(item => item.AnimalId == seed.AnimalId
                && item.EventType == TimelineEventTypes.StatusChanged);

        Assert.Equal(seed.SoldStatusId, evt.RelatedEntityId);
        Assert.Equal(TimelineRelatedEntityTypes.AnimalStatus, evt.RelatedEntityType);
    }

    [Fact]
    public async Task ChangeStatus_RecordsTheStatusMovedTo_Bulk()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);

        var result = await CreateService(context).BulkChangeStatusAsync(
            seed.FarmId, new BulkStatusChangeRequest
            {
                AnimalIds = new List<Guid> { seed.AnimalId },
                NewStatusId = seed.SoldStatusId,
                Reason = "Herd sale"
            });

        Assert.True(result.IsSuccess, result.Error?.Message);

        var evt = await context.AnimalTimelineEvents
            .SingleAsync(item => item.AnimalId == seed.AnimalId
                && item.EventType == TimelineEventTypes.StatusChanged);

        Assert.Equal(seed.SoldStatusId, evt.RelatedEntityId);
        Assert.Equal(TimelineRelatedEntityTypes.AnimalStatus, evt.RelatedEntityType);
    }
}
