using System.Diagnostics;
using FMS.Application.Feed;
using FMS.Application.Jobs;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Jobs;

/// <summary>
/// Materialises feeding tasks for the current UTC day.
///
/// This is deliberately a scheduling wrapper only: it calls the same
/// <see cref="IFeedService.GenerateTasksAsync"/> the manual
/// <c>POST /api/farm/{farmId}/feeding-tasks/generate</c> endpoint calls, with no
/// business logic of its own. That is what makes the scheduled and manual paths
/// provably identical, and it keeps the de-duplication (schedules that already
/// have a task for the date are skipped) in exactly one place.
///
/// Intended to be invoked by Hangfire once per farm, so each farm runs in its
/// own service scope (own <c>FmsDbContext</c>), retries independently, and
/// cannot roll back another farm's work.
/// </summary>
public class FeedingTaskGenerationJob
{
    private readonly IFeedService _feedService;
    private readonly ILogger<FeedingTaskGenerationJob> _logger;

    public FeedingTaskGenerationJob(IFeedService feedService, ILogger<FeedingTaskGenerationJob> logger)
    {
        _feedService = feedService;
        _logger = logger;
    }

    /// <summary>
    /// Generates the farm's feeding tasks for <paramref name="taskDate"/> (or today).
    /// </summary>
    /// <returns>The number of tasks created (zero when already generated).</returns>
    /// <exception cref="InvalidOperationException">
    /// The generation failed. The failure is logged first, then rethrown so the
    /// scheduler records it and applies the retry policy — never swallowed.
    /// </exception>
    public async Task<int> ExecuteAsync(Guid farmId, DateTime? taskDate = null)
    {
        var date = (taskDate ?? DateTime.UtcNow).Date;
        var startedAt = Stopwatch.GetTimestamp();

        var result = await _feedService.GenerateTasksAsync(farmId, new GenerateFeedingTasksRequest { Date = date });

        if (!result.IsSuccess)
        {
            // Logged with the structured shape used across the job layer: the
            // job name and farm id are named properties, so a failure is
            // greppable per farm in the Serilog console/file sinks.
            _logger.LogError(
                "Job {JobName} failed for farm {FarmId} for {TaskDate}: {ErrorCode} - {ErrorMessage}",
                JobNames.FeedingTaskGeneration,
                farmId,
                date,
                result.Error?.Code,
                result.Error?.Message);

            throw new InvalidOperationException(
                $"Job {JobNames.FeedingTaskGeneration} failed for farm {farmId} for {date:yyyy-MM-dd}: " +
                $"{result.Error?.Code} - {result.Error?.Message}");
        }

        var createdCount = result.Value?.Count ?? 0;
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        _logger.LogInformation(
            "Job {JobName} completed for farm {FarmId} in {DurationMs}ms ({CreatedCount} feeding tasks created for {TaskDate})",
            JobNames.FeedingTaskGeneration,
            farmId,
            Math.Round(elapsedMs, 1),
            createdCount,
            date);

        return createdCount;
    }
}
