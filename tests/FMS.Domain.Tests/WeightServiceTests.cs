using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class WeightServiceTests
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

    private static async Task<(Guid farmId, Guid animalId)> SeedAnimalAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "W-001",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        return (farm.Id, animal.Id);
    }

    [Fact]
    public async Task AddWeight_Succeeds_AndWritesTimelineEvent()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var result = await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 250.5m, RecordedAt = DateTime.UtcNow.AddDays(-10) });

        Assert.True(result.IsSuccess);
        Assert.Equal(250.5m, result.Value!.WeightKg);

        var events = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.WeightRecorded)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal("WeightRecord", events[0].RelatedEntityType);
        Assert.Contains("250.5", events[0].Title);
    }

    [Fact]
    public async Task AddWeight_NonPositive_ReturnsValidation()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var result = await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 0 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task AddWeight_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var result = await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 100, RecordedAt = DateTime.UtcNow.AddDays(30) });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task GetWeights_ReturnsHistory_WithChangeFromPrevious()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 200, RecordedAt = DateTime.UtcNow.AddDays(-20) });
        await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 230, RecordedAt = DateTime.UtcNow.AddDays(-10) });
        await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 250, RecordedAt = DateTime.UtcNow.AddDays(-1) });

        var result = await service.GetWeightsAsync(farmId, animalId, 1, 20);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);

        var newest = result.Value.Items[0];
        Assert.Equal(250m, newest.WeightKg);
        Assert.Equal(20m, newest.ChangeFromPreviousKg);

        var oldest = result.Value.Items[2];
        Assert.Null(oldest.ChangeFromPreviousKg);
    }

    [Fact]
    public async Task GetWeightReport_ComputesGainAndAverageDailyGain()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 200, RecordedAt = DateTime.UtcNow.AddDays(-100) });
        await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 300, RecordedAt = DateTime.UtcNow });

        var result = await service.GetWeightReportAsync(farmId, animalId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.RecordCount);
        Assert.Equal(200m, result.Value.FirstWeightKg);
        Assert.Equal(300m, result.Value.LastWeightKg);
        Assert.Equal(100m, result.Value.TotalGainKg);
        Assert.NotNull(result.Value.AverageDailyGainKg);
        Assert.InRange(result.Value.AverageDailyGainKg!.Value, 0.9, 1.1);
        Assert.Equal(2, result.Value.Points.Count);
    }

    [Fact]
    public async Task GetWeightReport_NoRecords_ReturnsEmptyReport()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var result = await service.GetWeightReportAsync(farmId, animalId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.RecordCount);
        Assert.Null(result.Value.TotalGainKg);
        Assert.Empty(result.Value.Points);
    }

    [Fact]
    public async Task UpdateWeight_ChangesValue_AndWritesTimelineEvent()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var added = await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 200, RecordedAt = DateTime.UtcNow.AddDays(-5) });

        var result = await service.UpdateWeightAsync(farmId, animalId, added.Value!.Id,
            new UpdateWeightRecordRequest { WeightKg = 210, RecordedAt = added.Value.RecordedAt });

        Assert.True(result.IsSuccess);
        Assert.Equal(210m, result.Value!.WeightKg);

        var events = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.WeightUpdated)
            .ToListAsync();
        Assert.Single(events);
        Assert.Contains("200", events[0].Title);
        Assert.Contains("210", events[0].Title);
    }

    [Fact]
    public async Task DeleteWeight_RemovesRecord_AndWritesTimelineEvent()
    {
        using var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = CreateService(context);

        var added = await service.AddWeightAsync(farmId, animalId,
            new CreateWeightRecordRequest { WeightKg = 200, RecordedAt = DateTime.UtcNow.AddDays(-5) });

        var deleteResult = await service.DeleteWeightAsync(farmId, animalId, added.Value!.Id);
        Assert.True(deleteResult.IsSuccess);

        var remaining = await context.WeightRecords.CountAsync(w => w.AnimalId == animalId);
        Assert.Equal(0, remaining);

        var events = await context.AnimalTimelineEvents
            .Where(e => e.EventType == TimelineEventTypes.WeightDeleted)
            .ToListAsync();
        Assert.Single(events);
    }
}
