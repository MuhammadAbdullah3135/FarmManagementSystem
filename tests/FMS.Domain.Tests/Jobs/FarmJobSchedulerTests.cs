using FMS.API.Jobs;
using FMS.Application.Jobs;
using FMS.Domain.Entities;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// The only cross-farm code in the job system is the fan-out, and it must touch
/// identifiers only: every unit of real work is then enqueued per farm, so it
/// runs in its own scope and fails and retries independently.
/// </summary>
public class FarmJobSchedulerTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class StubFarmDirectory : IFarmDirectory
    {
        private readonly IReadOnlyList<Guid> _farmIds;

        public StubFarmDirectory(params Guid[] farmIds) => _farmIds = farmIds;

        public Task<IReadOnlyList<Guid>> GetActiveFarmIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_farmIds);
    }

    /// <summary>Captures the jobs Hangfire would have created.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Created { get; } = new();

        public string Create(Job job, IState state)
        {
            Created.Add(job);
            return Guid.NewGuid().ToString("N");
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static (FarmJobScheduler Scheduler, RecordingJobClient Jobs, CapturingLoggerProvider Logs) CreateScheduler(
        params Guid[] farmIds)
    {
        var (logger, logs) = TestLoggers.Create<FarmJobScheduler>();
        var client = new RecordingJobClient();
        return (new FarmJobScheduler(new StubFarmDirectory(farmIds), client, logger), client, logs);
    }

    [Fact]
    public async Task EnqueueFeedingTaskGenerationAsync_EnqueuesOneJobPerFarmCarryingThatFarmId()
    {
        var farmA = Guid.NewGuid();
        var farmB = Guid.NewGuid();
        var farmC = Guid.NewGuid();
        var (scheduler, jobs, logs) = CreateScheduler(farmA, farmB, farmC);

        await scheduler.EnqueueFeedingTaskGenerationAsync();

        Assert.Equal(3, jobs.Created.Count);
        Assert.All(jobs.Created, job => Assert.Equal(typeof(FeedingTaskGenerationJob), job.Type));
        Assert.Equal(
            new object[] { farmA, farmB, farmC },
            jobs.Created.Select(job => job.Args[0]).ToArray());

        var info = Assert.Single(logs.ForLevel(LogLevel.Information));
        Assert.Contains("feeding-task-generation-fan-out", info.Message);
        Assert.Contains("3 farm jobs", info.Message);
    }

    [Fact]
    public async Task EnqueueHealthStatusRecalculationAsync_EnqueuesOneJobPerFarmCarryingThatFarmId()
    {
        var farmA = Guid.NewGuid();
        var farmB = Guid.NewGuid();
        var (scheduler, jobs, _) = CreateScheduler(farmA, farmB);

        await scheduler.EnqueueHealthStatusRecalculationAsync();

        Assert.Equal(2, jobs.Created.Count);
        Assert.All(jobs.Created, job => Assert.Equal(typeof(HealthStatusRecalculationJob), job.Type));
        Assert.Equal(
            new object[] { farmA, farmB },
            jobs.Created.Select(job => job.Args[0]).ToArray());
    }

    [Fact]
    public async Task EnqueueFeedingTaskGenerationAsync_WithNoFarms_EnqueuesNothing()
    {
        var (scheduler, jobs, logs) = CreateScheduler();

        await scheduler.EnqueueFeedingTaskGenerationAsync();

        Assert.Empty(jobs.Created);
        var info = Assert.Single(logs.ForLevel(LogLevel.Information));
        Assert.Contains("0 farm jobs across 0 active farms", info.Message);
    }
}

/// <summary>The farm directory is the fan-out's only global read, so it is pinned down here.</summary>
public class FarmDirectoryTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task GetActiveFarmIdsAsync_ReturnsOnlyActiveNonDeletedFarms()
    {
        using var context = CreateContext();
        var accountId = Guid.NewGuid();

        var active = new Farm { Id = Guid.NewGuid(), AccountId = accountId, Name = "Active", IsActive = true };
        var inactive = new Farm { Id = Guid.NewGuid(), AccountId = accountId, Name = "Inactive", IsActive = false };
        var deleted = new Farm { Id = Guid.NewGuid(), AccountId = accountId, Name = "Deleted", IsActive = true, IsDeleted = true };

        context.Farms.AddRange(active, inactive, deleted);
        await context.SaveChangesAsync();

        var farmIds = await new FarmDirectory(context).GetActiveFarmIdsAsync();

        Assert.Equal(new[] { active.Id }, farmIds);
    }
}
