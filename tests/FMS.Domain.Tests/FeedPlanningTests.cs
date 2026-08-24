using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FeedPlanningTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static FeedService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(
        Guid FarmId, Guid CowTypeId, Guid GoatTypeId, Guid AlfalfaId, Guid SilageId,
        Guid Cow1Id, Guid Cow2Id);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var cowType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var goatType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Goat" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };

        var animals = new List<Animal>();
        for (var i = 0; i < 2; i++)
        {
            animals.Add(new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                TagNumber = $"C-{i + 1:D3}",
                AnimalTypeId = cowType.Id,
                SexOptionId = sex.Id,
                AnimalStatusId = status.Id
            });
        }
        animals.Add(new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "G-001",
            AnimalTypeId = goatType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        });

        var alfalfa = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Alfalfa Hay",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 5m
        };
        var silage = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Corn Silage",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 2m
        };

        context.Farms.Add(farm);
        context.AnimalTypes.AddRange(cowType, goatType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.AddRange(animals);
        context.FeedTypes.AddRange(alfalfa, silage);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, cowType.Id, goatType.Id, alfalfa.Id, silage.Id, animals[0].Id, animals[1].Id);
    }

    private async Task<(Guid planId, Guid scheduleId)> SeedPlanWithScheduleAsync(FmsDbContext context, SeedData seed)
    {
        var service = CreateService(context);
        var plan = await service.CreateDietPlanAsync(seed.FarmId, new CreateDietPlanRequest
        {
            Name = "Adult Cow Diet",
            AnimalTypeId = seed.CowTypeId,
            Items = new List<AddDietPlanItemRequest>
            {
                new() { FeedTypeId = seed.AlfalfaId, QuantityPerFeeding = 3 },
                new() { FeedTypeId = seed.SilageId, QuantityPerFeeding = 5 }
            }
        });
        Assert.True(plan.IsSuccess, plan.Error?.ToString());

        var schedule = await service.CreateScheduleAsync(seed.FarmId, new CreateFeedingScheduleRequest
        {
            DietPlanId = plan.Value!.Id,
            TimeOfDay = "07:00",
            Label = "Morning"
        });
        Assert.True(schedule.IsSuccess, schedule.Error?.ToString());

        return (plan.Value.Id, schedule.Value!.Id);
    }

    [Fact]
    public async Task CreateDietPlan_WithItems_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateDietPlanAsync(seed.FarmId, new CreateDietPlanRequest
        {
            Name = "Adult Cow Morning Diet",
            AnimalTypeId = seed.CowTypeId,
            MinWeightKg = 200,
            MaxWeightKg = 600,
            Items = new List<AddDietPlanItemRequest>
            {
                new() { FeedTypeId = seed.AlfalfaId, QuantityPerFeeding = 3 }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Adult Cow Morning Diet", result.Value!.Name);
        Assert.Equal("Cow", result.Value.AnimalTypeName);
        Assert.Single(result.Value.Items);
        Assert.Equal("Alfalfa Hay", result.Value.Items[0].FeedTypeName);
    }

    [Fact]
    public async Task CreateDietPlan_WeightRangeInverted_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateDietPlanAsync(seed.FarmId, new CreateDietPlanRequest
        {
            Name = "Bad Range",
            MinWeightKg = 500,
            MaxWeightKg = 200
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateDietPlan_BreedMismatch_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var breed = new Breed { Id = Guid.NewGuid(), AnimalTypeId = seed.GoatTypeId, Name = "Boer" };
        context.Breeds.Add(breed);
        await context.SaveChangesAsync();

        var result = await service.CreateDietPlanAsync(seed.FarmId, new CreateDietPlanRequest
        {
            Name = "Mismatched",
            AnimalTypeId = seed.CowTypeId,
            BreedId = breed.Id
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task AddDietPlanItem_DuplicateFeedType_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, _) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var result = await service.AddDietPlanItemAsync(seed.FarmId, planId,
            new AddDietPlanItemRequest { FeedTypeId = seed.AlfalfaId, QuantityPerFeeding = 9 });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task RemoveDietPlanItem_Works()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, _) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var plan = await service.GetDietPlanByIdAsync(seed.FarmId, planId);
        var itemId = plan.Value!.Items[0].Id;

        var result = await service.RemoveDietPlanItemAsync(seed.FarmId, planId, itemId);

        Assert.True(result.IsSuccess);
        var updated = await service.GetDietPlanByIdAsync(seed.FarmId, planId);
        Assert.Single(updated.Value!.Items);
    }

    [Fact]
    public async Task CreateSchedule_ParsesTime_AndDetectsDuplicate()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, _) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var evening = await service.CreateScheduleAsync(seed.FarmId, new CreateFeedingScheduleRequest
        {
            DietPlanId = planId,
            TimeOfDay = "16:30",
            Label = "Evening"
        });
        Assert.True(evening.IsSuccess);
        Assert.Equal("16:30", evening.Value!.TimeOfDay);

        var duplicate = await service.CreateScheduleAsync(seed.FarmId, new CreateFeedingScheduleRequest
        {
            DietPlanId = planId,
            TimeOfDay = "07:00"
        });
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);

        var invalid = await service.CreateScheduleAsync(seed.FarmId, new CreateFeedingScheduleRequest
        {
            DietPlanId = planId,
            TimeOfDay = "7am"
        });
        Assert.False(invalid.IsSuccess);
        Assert.Equal("Validation", invalid.Error!.Code);
    }

    [Fact]
    public async Task GenerateTasks_CreatesPendingTasks_WithTargetCounts()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, _) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var result = await service.GenerateTasksAsync(seed.FarmId,
            new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value!);
        Assert.Equal(planId, task.DietPlanId);
        Assert.Equal("07:00", task.TimeOfDay);
        Assert.Equal("Pending", task.StatusName);
        Assert.Equal(2, task.TargetAnimalCount);
        Assert.Equal(2, task.Items.Count);
    }

    [Fact]
    public async Task GenerateTasks_Idempotent_SecondCallReturnsEmpty()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });
        var second = await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });

        Assert.True(second.IsSuccess);
        Assert.Empty(second.Value!);
        Assert.Equal(1, await context.FeedingTasks.CountAsync());
    }

    [Fact]
    public async Task GenerateTasks_InactiveScheduleOrPlan_Skipped()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, scheduleId) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        await service.UpdateScheduleAsync(seed.FarmId, scheduleId, new UpdateFeedingScheduleRequest { IsActive = false });
        var result = await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });
        Assert.Empty(result.Value!);

        await service.UpdateScheduleAsync(seed.FarmId, scheduleId, new UpdateFeedingScheduleRequest { IsActive = true });
        await service.UpdateDietPlanAsync(seed.FarmId, planId, new UpdateDietPlanRequest
        {
            Name = "Adult Cow Diet",
            AnimalTypeId = seed.CowTypeId,
            IsActive = false
        });
        var result2 = await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });
        Assert.Empty(result2.Value!);
    }

    [Fact]
    public async Task GenerateTasks_PastDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var result = await service.GenerateTasksAsync(seed.FarmId,
            new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date.AddDays(-1) });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CompleteAndSkipTask_Transitions_AndGuardAgainstDoubleClose()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var tasks = await service.GenerateTasksAsync(seed.FarmId,
            new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });
        var taskId = tasks.Value![0].Id;

        var complete = await service.CompleteTaskAsync(seed.FarmId, taskId,
            new CompleteFeedingTaskRequest { Notes = "All cows fed" });
        Assert.True(complete.IsSuccess);
        Assert.Equal("Completed", complete.Value!.StatusName);
        Assert.NotNull(complete.Value.CompletedAt);
        Assert.Equal("All cows fed", complete.Value.Notes);

        var again = await service.CompleteTaskAsync(seed.FarmId, taskId, new CompleteFeedingTaskRequest());
        Assert.False(again.IsSuccess);
        Assert.Equal("Conflict", again.Error!.Code);

        // second task path: skip flow on a fresh day
        var tasksDay2 = await service.GenerateTasksAsync(seed.FarmId,
            new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date.AddDays(1) });
        var skip = await service.SkipTaskAsync(seed.FarmId, tasksDay2.Value![0].Id, new CompleteFeedingTaskRequest());
        Assert.True(skip.IsSuccess);
        Assert.Equal("Skipped", skip.Value!.StatusName);
    }

    [Fact]
    public async Task DeleteDietPlan_WithTasks_ReturnsConflict_WithoutTasks_Cascades()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (planId, _) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date });

        var blocked = await service.DeleteDietPlanAsync(seed.FarmId, planId);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("Conflict", blocked.Error!.Code);

        var tasks = await context.FeedingTasks.ToListAsync();
        context.FeedingTasks.RemoveRange(tasks);
        await context.SaveChangesAsync();

        var deleted = await service.DeleteDietPlanAsync(seed.FarmId, planId);
        Assert.True(deleted.IsSuccess);
        Assert.False(await context.DietPlans.AnyAsync(p => p.Id == planId));
        Assert.Empty(await context.DietPlanItems.Where(i => i.DietPlanId == planId).ToListAsync());
        Assert.Empty(await context.FeedingSchedules.Where(s => s.DietPlanId == planId).ToListAsync());
    }

    [Fact]
    public async Task GetTasks_FilterByDateAndStatus_Paged()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var (_, scheduleId) = await SeedPlanWithScheduleAsync(context, seed);
        var service = CreateService(context);

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        await service.CreateScheduleAsync(seed.FarmId, new CreateFeedingScheduleRequest
        {
            DietPlanId = (await service.GetSchedulesAsync(seed.FarmId, null)).Value![0].DietPlanId,
            TimeOfDay = "16:00"
        });

        await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = today });
        await service.GenerateTasksAsync(seed.FarmId, new GenerateFeedingTasksRequest { Date = tomorrow });

        var allToday = await service.GetTasksAsync(seed.FarmId, new FeedingTaskListFilter { Date = today });
        Assert.Equal(2, allToday.Value!.TotalCount);

        var firstToday = allToday.Value.Items[0];
        await service.CompleteTaskAsync(seed.FarmId, firstToday.Id, new CompleteFeedingTaskRequest());

        var pendingToday = await service.GetTasksAsync(seed.FarmId, new FeedingTaskListFilter { Date = today, Status = "pending" });
        Assert.Equal(1, pendingToday.Value!.TotalCount);

        var completedToday = await service.GetTasksAsync(seed.FarmId, new FeedingTaskListFilter { Date = today, Status = "Completed" });
        Assert.Equal(1, completedToday.Value!.TotalCount);

        var invalidStatus = await service.GetTasksAsync(seed.FarmId, new FeedingTaskListFilter { Status = "nope" });
        Assert.False(invalidStatus.IsSuccess);
    }
}
