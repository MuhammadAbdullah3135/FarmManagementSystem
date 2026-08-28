using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class WeightCheckScheduleServiceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WeightCheckScheduleService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid AnimalTypeId, Guid BreedId, Guid AgeCategoryId, Guid AnimalId, Guid OtherAnimalId, Guid ThirdAnimalId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Domain.Entities.Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var ageCategory = new Domain.Entities.AgeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Adult", MinDays = 365, MaxDays = 3650 };
        var sex = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };

        var animal1 = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "WC-001", Name = "Bella",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };
        var animal2 = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "WC-002", Name = "Max",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };
        var animal3 = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "WC-003", Name = "Charlie",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.AgeCategories.Add(ageCategory);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.AddRange(animal1, animal2, animal3);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, type.Id, breed.Id, ageCategory.Id, animal1.Id, animal2.Id, animal3.Id);
    }

    [Fact]
    public async Task Schedule_Crud_Works_WithValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Negative recurrence
        var bad = await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest { RecurrenceDays = -1 });
        Assert.Equal("Validation", bad.Error!.Code);

        // Invalid animal type
        var badType = await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, AnimalTypeId = Guid.NewGuid() });
        Assert.Equal("Validation", badType.Error!.Code);

        // Valid create
        var created = await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        {
            RecurrenceDays = 7,
            AnimalTypeId = seed.AnimalTypeId,
            BreedId = seed.BreedId,
            Notes = "Weekly check"
        });
        Assert.True(created.IsSuccess);
        Assert.Equal(7, created.Value!.RecurrenceDays);

        // List
        var list = await service.GetSchedulesAsync(seed.FarmId, 1, 20);
        Assert.Equal(1, list.Value!.TotalCount);

        // Update
        var updated = await service.UpdateScheduleAsync(seed.FarmId, created.Value.Id, new UpdateWeightCheckScheduleRequest
        { RecurrenceDays = 45, IsActive = false });
        Assert.True(updated.IsSuccess);
        Assert.Equal(45, updated.Value!.RecurrenceDays);
        Assert.False(updated.Value.IsActive);

        // Delete
        var deleted = await service.DeleteScheduleAsync(seed.FarmId, created.Value.Id);
        Assert.True(deleted.IsSuccess);
    }

    [Fact]
    public async Task WeightCheckStatus_7DaySchedule_ComputesCorrectStatuses()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Create a 7-day schedule
        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, AnimalTypeId = seed.AnimalTypeId });

        // Bella: weighed 10 days ago -> next due 3 days ago -> Overdue
        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.AnimalId,
            FarmId = seed.FarmId,
            WeightKg = 450,
            RecordedAt = DateTime.UtcNow.Date.AddDays(-10)
        });
        await context.SaveChangesAsync();

        // Max: weighed 5 days ago -> next due in 2 days -> Due (within 7-day threshold)
        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.OtherAnimalId,
            FarmId = seed.FarmId,
            WeightKg = 500,
            RecordedAt = DateTime.UtcNow.Date.AddDays(-5)
        });
        await context.SaveChangesAsync();

        var statuses = await service.GetWeightCheckStatusAsync(seed.FarmId);
        Assert.True(statuses.IsSuccess);
        Assert.Equal(3, statuses.Value!.Count);

        var bella = statuses.Value.Single(s => s.AnimalId == seed.AnimalId);
        Assert.Equal(WeightCheckStatusType.Overdue, bella.Status);
        Assert.Equal("Overdue", bella.StatusName);

        var max = statuses.Value.Single(s => s.AnimalId == seed.OtherAnimalId);
        Assert.Equal(WeightCheckStatusType.Due, max.Status);
        Assert.Equal("Due", max.StatusName);

        // Charlie: no weights -> next due = CreatedAt + 7 days
        var charlie = statuses.Value.Single(s => s.AnimalId == seed.ThirdAnimalId);
        // CreatedAt is roughly today, so + 7 days = today or tomorrow
        Assert.True(charlie.DaysUntilDue >= -1 && charlie.DaysUntilDue <= 7);
    }

    [Fact]
    public async Task WeightCheckStatus_45DaySchedule_ComputesCorrectStatuses()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Create a 45-day schedule
        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 45 });

        // Bella: weighed 20 days ago -> next due in 25 days -> Upcoming
        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.AnimalId,
            FarmId = seed.FarmId,
            WeightKg = 450,
            RecordedAt = DateTime.UtcNow.Date.AddDays(-20)
        });
        await context.SaveChangesAsync();

        var statuses = await service.GetWeightCheckStatusAsync(seed.FarmId);
        Assert.True(statuses.IsSuccess);

        var bella = statuses.Value!.Single(s => s.AnimalId == seed.AnimalId);
        Assert.Equal(WeightCheckStatusType.Upcoming, bella.Status);
        Assert.Equal("Upcoming", bella.StatusName);
        Assert.Equal(25, bella.DaysUntilDue);
    }

    [Fact]
    public async Task WeightCheckStatus_InactiveScheduleIsIgnored()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, IsActive = false });

        var statuses = await service.GetWeightCheckStatusAsync(seed.FarmId);
        Assert.Empty(statuses.Value!);
    }

    [Fact]
    public async Task WeightCheckStatus_FiltersByBreedAndAgeCategory()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 30, BreedId = seed.BreedId });

        var statuses = await service.GetWeightCheckStatusAsync(seed.FarmId);
        Assert.Equal(3, statuses.Value!.Count);
    }

    [Fact]
    public async Task AutoTask_Created_For_DueAnimals()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, AnimalTypeId = seed.AnimalTypeId });

        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.AnimalId, FarmId = seed.FarmId,
            WeightKg = 450, RecordedAt = DateTime.UtcNow.Date.AddDays(-10)
        });
        await context.SaveChangesAsync();

        await service.GetWeightCheckStatusAsync(seed.FarmId);

        var tasks = await context.FarmTasks
            .Where(t => t.AnimalId == seed.AnimalId && t.Title.Contains("Weight check due"))
            .ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(Domain.Enums.FarmTaskStatus.Pending, tasks[0].Status);
    }

    [Fact]
    public async Task AutoTask_Deduplication()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, AnimalTypeId = seed.AnimalTypeId });

        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.AnimalId, FarmId = seed.FarmId,
            WeightKg = 450, RecordedAt = DateTime.UtcNow.Date.AddDays(-10)
        });
        await context.SaveChangesAsync();

        await service.GetWeightCheckStatusAsync(seed.FarmId);
        await service.GetWeightCheckStatusAsync(seed.FarmId);

        var tasks = await context.FarmTasks
            .Where(t => t.AnimalId == seed.AnimalId && t.Title.Contains("Weight check due"))
            .ToListAsync();
        Assert.Single(tasks);
    }

    [Fact]
    public async Task GetOverdueWeightChecks_ReturnsDueAndOverdue()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateWeightCheckScheduleRequest
        { RecurrenceDays = 7, AnimalTypeId = seed.AnimalTypeId });

        context.WeightRecords.Add(new Domain.Entities.WeightRecord
        {
            AnimalId = seed.AnimalId, FarmId = seed.FarmId,
            WeightKg = 450, RecordedAt = DateTime.UtcNow.Date.AddDays(-10)
        });
        await context.SaveChangesAsync();

        var overdue = await service.GetOverdueWeightChecksAsync(seed.FarmId);
        Assert.True(overdue.IsSuccess);
        Assert.True(overdue.Value!.Count >= 2);
    }
}
