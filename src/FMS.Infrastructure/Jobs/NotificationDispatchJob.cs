using System.Diagnostics;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Jobs;

/// <summary>
/// The scheduled entry point for notification dispatch.
///
/// A thin wrapper around <see cref="INotificationDispatcher"/> in the same shape
/// as the other two jobs: the work itself lives in a plain, DI-resolvable class
/// that tests can invoke directly, and this adds only the job-level logging and
/// the fail-loudly contract the scheduler depends on.
///
/// Intended to be invoked by Hangfire once per farm, so each farm runs in its own
/// service scope (own <c>FmsDbContext</c>), retries independently, and cannot block
/// or roll back another farm's notifications.
/// </summary>
public class NotificationDispatchJob
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<NotificationDispatchJob> _logger;

    public NotificationDispatchJob(
        INotificationDispatcher dispatcher,
        ILogger<NotificationDispatchJob> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>Reconciles the farm's notifications and delivers any digests.</summary>
    /// <exception cref="Exception">
    /// The dispatch failed. Logged first with the structured job/farm shape used
    /// across this layer, then rethrown so the scheduler records it and applies the
    /// retry policy — never swallowed.
    /// </exception>
    public async Task<NotificationDispatchResult> ExecuteAsync(Guid farmId)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = await _dispatcher.ExecuteAsync(farmId);

            _logger.LogInformation(
                "Job {JobName} completed for farm {FarmId} in {DurationMs}ms "
                + "({CreatedCount} created, {UpdatedCount} updated, {ResolvedCount} resolved, "
                + "{EmailsSent} digest(s) sent, {EmailFailures} failed, {SkippedCount} alert type(s) skipped)",
                JobNames.NotificationDispatch,
                farmId,
                Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1),
                result.Created,
                result.Updated,
                result.Resolved,
                result.EmailsSent,
                result.EmailFailures,
                result.SkippedAlertTypes.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Job {JobName} failed for farm {FarmId}: {ExceptionType} - {ExceptionMessage}",
                JobNames.NotificationDispatch,
                farmId,
                ex.GetType().Name,
                ex.Message);

            throw;
        }
    }
}
