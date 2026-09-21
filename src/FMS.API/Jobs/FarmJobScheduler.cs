using FMS.Application.Jobs;
using FMS.Infrastructure.Jobs;
using Hangfire;

namespace FMS.API.Jobs;

/// <summary>
/// Farm fan-out: the recurring jobs registered with Hangfire.
///
/// These deliberately do almost nothing themselves. They read the active farm
/// ids and enqueue one job <em>per farm</em>, which is the multi-tenancy
/// guarantee for work that runs outside a request:
///
/// <list type="bullet">
/// <item>each farm's job executes in its own service scope, so it gets its own
/// <c>FmsDbContext</c> and its own transaction — one farm's failure can neither
/// roll back nor block another farm;</item>
/// <item>retries and backoff are per farm, so a single problematic farm cannot
/// stall the rest;</item>
/// <item>the target code paths take an explicit farm id, so no job body ever
/// queries across farms.</item>
/// </list>
/// </summary>
public class FarmJobScheduler
{
    private readonly IFarmDirectory _farmDirectory;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<FarmJobScheduler> _logger;

    public FarmJobScheduler(
        IFarmDirectory farmDirectory,
        IBackgroundJobClient jobs,
        ILogger<FarmJobScheduler> logger)
    {
        _farmDirectory = farmDirectory;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task EnqueueFeedingTaskGenerationAsync()
    {
        var farmIds = await _farmDirectory.GetActiveFarmIdsAsync();

        foreach (var farmId in farmIds)
        {
            // Copied to a loop-local so the enqueued argument is the value for
            // this iteration, never the last one seen.
            var targetFarmId = farmId;
            _jobs.Enqueue<FeedingTaskGenerationJob>(job => job.ExecuteAsync(targetFarmId, null));
        }

        _logger.LogInformation(
            "Job {JobName} enqueued {EnqueuedCount} farm jobs across {FarmCount} active farms",
            JobNames.FeedingTaskGenerationFanOut,
            farmIds.Count,
            farmIds.Count);
    }

    public async Task EnqueueHealthStatusRecalculationAsync()
    {
        var farmIds = await _farmDirectory.GetActiveFarmIdsAsync();

        foreach (var farmId in farmIds)
        {
            var targetFarmId = farmId;
            _jobs.Enqueue<HealthStatusRecalculationJob>(job => job.ExecuteAsync(targetFarmId));
        }

        _logger.LogInformation(
            "Job {JobName} enqueued {EnqueuedCount} farm jobs across {FarmCount} active farms",
            JobNames.HealthStatusRecalculationFanOut,
            farmIds.Count,
            farmIds.Count);
    }

    public async Task EnqueueNotificationDispatchAsync()
    {
        var farmIds = await _farmDirectory.GetActiveFarmIdsAsync();

        foreach (var farmId in farmIds)
        {
            var targetFarmId = farmId;
            _jobs.Enqueue<NotificationDispatchJob>(job => job.ExecuteAsync(targetFarmId));
        }

        _logger.LogInformation(
            "Job {JobName} enqueued {EnqueuedCount} farm jobs across {FarmCount} active farms",
            JobNames.NotificationDispatchFanOut,
            farmIds.Count,
            farmIds.Count);
    }
}
