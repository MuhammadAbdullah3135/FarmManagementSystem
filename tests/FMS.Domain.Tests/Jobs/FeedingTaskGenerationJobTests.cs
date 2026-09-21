using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// The scheduled feed-task job must be a scheduling wrapper around the existing,
/// already-tested generation logic — never a second implementation. These tests
/// pin that down: same output as the manual path for the same input, still
/// idempotent, still confined to one farm, and loud when it fails.
/// </summary>
public class FeedingTaskGenerationJobTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FeedService CreateFeedService(FmsDbContext context) => new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? GetUserId() => Guid.NewGuid();
    }

    private sealed record SeededFarm(Guid FarmId, Guid AnimalTypeId);

    private static async Task<SeededFarm> SeedFarmAsync(FmsDbContext context, string name, int animalCount)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = name };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var breed = new Breed { Id = Guid.NewGuid(), Name = "Holstein" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);

        for (var i = 0; i < animalCount; i++)
        {
            context.Animals.Add(new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                TagNumber = $"{name.Replace(" ", string.Empty)}-{i:D3}",
                AnimalTypeId = animalType.Id,
                BreedId = breed.Id,
                SexOptionId = sex.Id,
                AnimalStatusId = status.Id,
                DateOfBirth = DateTime.UtcNow.AddYears(-2)
            });
        }

        await context.SaveChangesAsync();
        return new SeededFarm(farm.Id, animalType.Id);
    }

    /// <summary>Gives a farm the same diet plan and two feeding schedules.</summary>
    private static async Task SeedPlanWithSchedulesAsync(IFeedService service, SeededFarm farm)
    {
        var farmId = farm.FarmId;
        var feedType = await service.CreateFeedTypeAsync(farmId, new CreateFeedTypeRequest
        {
            Name = "Hay",
            Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram,
            CostPerUnit = 3m
        });
        Assert.True(feedType.IsSuccess, feedType.Error?.ToString());

        var plan = await service.CreateDietPlanAsync(farmId, new CreateDietPlanRequest
        {
            Name = "Adult Cattle Diet",
            AnimalTypeId = farm.AnimalTypeId,
            Items = new List<AddDietPlanItemRequest>
            {
                new() { FeedTypeId = feedType.Value!.Id, QuantityPerFeeding = 4m }
            }
        });
        Assert.True(plan.IsSuccess, plan.Error?.ToString());

        foreach (var timeOfDay in new[] { "07:00", "16:30" })
        {
            var schedule = await service.CreateScheduleAsync(farmId, new CreateFeedingScheduleRequest
            {
                DietPlanId = plan.Value!.Id,
                TimeOfDay = timeOfDay
            });
            Assert.True(schedule.IsSuccess, schedule.Error?.ToString());
        }
    }

    /// <summary>
    /// Comparison shape: everything the manual endpoint returns except the
    /// identifiers, which legitimately differ between farms.
    /// </summary>
    private sealed record NormalizedTask(
        string TimeOfDay,
        string StatusName,
        int TargetAnimalCount,
        string DietPlanName,
        string Items);

    private static List<NormalizedTask> Normalize(IEnumerable<FeedingTaskDto> tasks) => tasks
        .OrderBy(t => t.TimeOfDay, StringComparer.Ordinal)
        .Select(t => new NormalizedTask(
            t.TimeOfDay,
            t.StatusName,
            t.TargetAnimalCount,
            t.DietPlanName,
            string.Join("; ", t.Items
                .Select(i => $"{i.FeedTypeName} {i.QuantityPerFeeding} {i.UnitName}")
                .OrderBy(s => s, StringComparer.Ordinal))))
        .ToList();

    [Fact]
    public async Task ExecuteAsync_ProducesTheSameTasksAsTheManualGenerationPath()
    {
        using var context = CreateContext();
        var service = CreateFeedService(context);

        var farmA = await SeedFarmAsync(context, "Farm A", animalCount: 3);
        var farmB = await SeedFarmAsync(context, "Farm B", animalCount: 3);
        await SeedPlanWithSchedulesAsync(service, farmA);
        await SeedPlanWithSchedulesAsync(service, farmB);

        var date = DateTime.UtcNow.Date.AddDays(1);

        // The manual path: literally what POST /api/farm/{farmId}/feed/tasks/generate does.
        var manual = await service.GenerateTasksAsync(farmA.FarmId, new GenerateFeedingTasksRequest { Date = date });
        Assert.True(manual.IsSuccess, manual.Error?.ToString());

        var (logger, _) = TestLoggers.Create<FeedingTaskGenerationJob>();
        var job = new FeedingTaskGenerationJob(service, logger);

        var createdCount = await job.ExecuteAsync(farmB.FarmId, date);

        var scheduled = await service.GetTasksAsync(farmB.FarmId, new FeedingTaskListFilter { Date = date });
        Assert.True(scheduled.IsSuccess);

        Assert.Equal(manual.Value!.Count, createdCount);
        Assert.Equal(Normalize(manual.Value!), Normalize(scheduled.Value!.Items));

        // Guards against a vacuous pass: both paths really resolved the same
        // non-zero target population.
        Assert.Equal(3, manual.Value![0].TargetAnimalCount);
    }

    [Fact]
    public async Task ExecuteAsync_TwiceForTheSameDay_CreatesNoDuplicates()
    {
        using var context = CreateContext();
        var service = CreateFeedService(context);
        var farm = await SeedFarmAsync(context, "Farm A", animalCount: 2);
        await SeedPlanWithSchedulesAsync(service, farm);

        var (logger, logs) = TestLoggers.Create<FeedingTaskGenerationJob>();
        var job = new FeedingTaskGenerationJob(service, logger);

        var first = await job.ExecuteAsync(farm.FarmId);
        var second = await job.ExecuteAsync(farm.FarmId);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(2, await context.FeedingTasks.CountAsync(t => t.FarmId == farm.FarmId));
        Assert.True(logs.Entries.Count(e => e.Level == LogLevel.Information) >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyWritesTasksForTheTargetFarm()
    {
        using var context = CreateContext();
        var service = CreateFeedService(context);

        var farmA = await SeedFarmAsync(context, "Farm A", animalCount: 2);
        var farmB = await SeedFarmAsync(context, "Farm B", animalCount: 2);
        await SeedPlanWithSchedulesAsync(service, farmA);
        await SeedPlanWithSchedulesAsync(service, farmB);

        var date = DateTime.UtcNow.Date.AddDays(1);
        var (logger, _) = TestLoggers.Create<FeedingTaskGenerationJob>();

        await new FeedingTaskGenerationJob(service, logger).ExecuteAsync(farmA.FarmId, date);

        Assert.Equal(2, await context.FeedingTasks.CountAsync(t => t.FarmId == farmA.FarmId));
        Assert.Equal(0, await context.FeedingTasks.CountAsync(t => t.FarmId == farmB.FarmId));
    }

    [Fact]
    public async Task ExecuteAsync_FailedGeneration_IsLoggedAndRethrown()
    {
        using var context = CreateContext();
        var service = CreateFeedService(context);
        var farm = await SeedFarmAsync(context, "Farm A", animalCount: 1);
        var farmId = farm.FarmId;
        await SeedPlanWithSchedulesAsync(service, farm);

        var (logger, logs) = TestLoggers.Create<FeedingTaskGenerationJob>();
        var job = new FeedingTaskGenerationJob(service, logger);

        // The generation service rejects past dates; the job must not paper over it.
        var pastDate = DateTime.UtcNow.Date.AddDays(-1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync(farmId, pastDate));

        Assert.Contains("feeding-task-generation", exception.Message);
        Assert.Contains(farmId.ToString(), exception.Message);

        // Visible: an Error-level entry naming the job and the farm.
        var error = Assert.Single(logs.ForLevel(LogLevel.Error));
        Assert.Contains("feeding-task-generation", error.Message);
        Assert.Contains(farmId.ToString(), error.Message);
        Assert.Contains("Validation", error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_LogsStructuredCompletionDetail()
    {
        using var context = CreateContext();
        var service = CreateFeedService(context);
        var farm = await SeedFarmAsync(context, "Farm A", animalCount: 2);
        await SeedPlanWithSchedulesAsync(service, farm);

        var (logger, logs) = TestLoggers.Create<FeedingTaskGenerationJob>();

        await new FeedingTaskGenerationJob(service, logger).ExecuteAsync(farm.FarmId);

        var info = Assert.Single(logs.ForLevel(LogLevel.Information));
        Assert.Contains("feeding-task-generation", info.Message);
        Assert.Contains(farm.FarmId.ToString(), info.Message);
        Assert.Contains("2 feeding tasks created", info.Message);
        // The job name and farm id are named log properties, not string-formatted text.
        Assert.Equal("FMS.Infrastructure.Jobs.FeedingTaskGenerationJob", info.Category);
    }
}
