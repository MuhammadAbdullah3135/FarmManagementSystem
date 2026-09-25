using System.Diagnostics;
using FMS.Application.Auth;
using FMS.Application.Dashboard;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Notifications;

/// <summary>
/// Reconciles one farm's notifications against its current alert conditions.
///
/// <para>
/// The alert conditions come from <see cref="IDashboardService.ComputeAlertSetAsync"/>,
/// the same computation the dashboard page runs — the notification system is a
/// durable record of those alerts, not a second definition of them.
/// </para>
///
/// <para>
/// <b>Reconciliation, not appending.</b> A run compares the current conditions
/// against the farm's open notifications and, for each recipient:
/// </para>
/// <list type="number">
/// <item>creates a notification for a condition they have no open row for;</item>
/// <item>refreshes the existing row when the condition's wording, severity or due
/// date changed — the same problem is not news the second time;</item>
/// <item>resolves an open row whose condition is no longer reported, which is what
/// lets the same condition alert again if it later returns.</item>
/// </list>
///
/// <para>
/// Runs once per farm in its own service scope, so nothing here ever reads across
/// farms and one farm's failure cannot affect another's.
/// </para>
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    /// <summary>One farm member as the dispatcher needs them: role for the audience check, email for the digest.</summary>
    private sealed class MemberRow
    {
        public Guid UserId { get; init; }
        public string Role { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
    }


    private readonly FmsDbContext _db;
    private readonly IDashboardService _dashboardService;
    private readonly IEmailService _emailService;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        FmsDbContext db,
        IDashboardService dashboardService,
        IEmailService emailService,
        IOptions<NotificationOptions> options,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _dashboardService = dashboardService;
        _emailService = emailService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NotificationDispatchResult> ExecuteAsync(
        Guid farmId,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var alertSetResult = await _dashboardService.ComputeAlertSetAsync(farmId);
        if (!alertSetResult.IsSuccess)
        {
            throw Fail(farmId, alertSetResult.Error?.Code, alertSetResult.Error?.Message);
        }

        var alertSet = alertSetResult.Value!;

        // A type whose metric failed is left completely alone: it is not evidence
        // that the condition cleared, and treating it as such would resolve every
        // live notification for that type on a momentary query failure.
        var skipped = alertSet.UnavailableAlertTypes
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var currentAlerts = alertSet.Alerts
            .Where(a => !string.IsNullOrEmpty(a.SourceKey))
            .Where(a => !skipped.Contains(a.AlertType, StringComparer.Ordinal))
            .ToList();

        var reconciledTypes = NotificationAlertTypes.All
            .Where(t => !skipped.Contains(t, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var members = await _db.UserFarms
            .AsNoTracking()
            .Where(uf => uf.FarmId == farmId)
            .Select(uf => new MemberRow
            {
                UserId = uf.UserId,
                Role = uf.Role,
                Email = uf.User.Email
            })
            .ToListAsync(cancellationToken);

        if (members.Count == 0)
        {
            _logger.LogInformation(
                "Job {JobName} for farm {FarmId} found no members to notify ({SkippedCount} alert type(s) skipped).",
                JobNames.NotificationDispatch, farmId, skipped.Count);

            return new NotificationDispatchResult(0, 0, 0, 0, 0, skipped);
        }

        var recipientIds = members.Select(m => m.UserId).Distinct().ToList();

        var openNotifications = await _db.Notifications
            .Where(n => n.FarmId == farmId
                && n.ResolvedAtUtc == null
                && recipientIds.Contains(n.UserId))
            .ToListAsync(cancellationToken);

        var preferences = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.FarmId == farmId && recipientIds.Contains(p.UserId))
            .ToListAsync(cancellationToken);

        var audienceByType = NotificationAlertTypes.All
            .ToDictionary(t => t, NotificationAlertTypes.GetAudience, StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        var created = new List<Notification>();
        var updatedCount = 0;

        foreach (var alert in currentAlerts)
        {
            var sourceKey = alert.SourceKey!;
            var audience = audienceByType.TryGetValue(alert.AlertType, out var roles)
                ? roles
                : NotificationAlertTypes.MemberRoles;

            foreach (var member in members)
            {
                if (!audience.Contains(member.Role))
                {
                    continue;
                }

                if (!IsInAppEnabled(preferences, member.UserId, alert.AlertType))
                {
                    continue;
                }

                var existing = openNotifications.FirstOrDefault(n =>
                    n.UserId == member.UserId
                    && string.Equals(n.SourceKey, sourceKey, StringComparison.Ordinal));

                if (existing is null)
                {
                    var notification = new Notification
                    {
                        Id = Guid.NewGuid(),
                        FarmId = farmId,
                        UserId = member.UserId,
                        AlertType = alert.AlertType,
                        Severity = alert.Severity,
                        SourceKey = sourceKey,
                        Title = alert.Title,
                        Message = alert.Message,
                        // The localisable half travels with the row: this notification may sit
                        // unread in a list for weeks, and the language it is read in is not the
                        // language it was generated in.
                        TitleKey = alert.TitleKey,
                        TitleArgsJson = MessageArgsJson.Serialize(alert.TitleArgs),
                        MessageKey = alert.MessageKey,
                        MessageArgsJson = MessageArgsJson.Serialize(alert.MessageArgs),
                        Link = alert.Link,
                        DueDate = alert.DueDate,
                        CreatedAt = now
                    };

                    _db.Notifications.Add(notification);
                    openNotifications.Add(notification);
                    created.Add(notification);
                    continue;
                }

                // Same condition, possibly newer words: refreshed in place so the
                // list never shows a stale due date, and counted separately because
                // only genuinely new conditions are worth notifying about.
                if (existing.Severity != alert.Severity
                    || existing.Title != alert.Title
                    || existing.Message != alert.Message
                    || existing.Link != alert.Link
                    || existing.DueDate != alert.DueDate)
                {
                    existing.Severity = alert.Severity;
                    existing.Title = alert.Title;
                    existing.Message = alert.Message;
                    // Refreshed with the text they belong to: leaving the old key would have the
                    // list render a stale sentence in the right language.
                    existing.TitleKey = alert.TitleKey;
                    existing.TitleArgsJson = MessageArgsJson.Serialize(alert.TitleArgs);
                    existing.MessageKey = alert.MessageKey;
                    existing.MessageArgsJson = MessageArgsJson.Serialize(alert.MessageArgs);
                    existing.Link = alert.Link;
                    existing.DueDate = alert.DueDate;
                    existing.ModifiedAt = now;
                    updatedCount++;
                }
            }
        }

        // Auto-resolve: a condition this type reports (or reported, for a type we
        // could evaluate) and that is no longer present is over.
        var currentKeys = currentAlerts
            .Select(a => (a.AlertType, a.SourceKey!))
            .ToHashSet();

        var resolvedCount = 0;
        foreach (var notification in openNotifications)
        {
            if (!reconciledTypes.Contains(notification.AlertType))
            {
                continue;
            }

            if (currentKeys.Contains((notification.AlertType, notification.SourceKey)))
            {
                continue;
            }

            notification.ResolvedAtUtc = now;
            notification.ModifiedAt = now;
            resolvedCount++;
        }

        if (created.Count > 0 || updatedCount > 0 || resolvedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var (emailsSent, emailFailures) = await DeliverDigestsAsync(
            farmId, members, preferences, created, now, cancellationToken);

        _logger.LogInformation(
            "Job {JobName} completed for farm {FarmId} in {DurationMs}ms "
            + "({CreatedCount} created, {UpdatedCount} updated, {ResolvedCount} resolved, "
            + "{EmailsSent} digest(s) sent, {EmailFailures} failed, {SkippedCount} alert type(s) skipped)",
            JobNames.NotificationDispatch,
            farmId,
            Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1),
            created.Count,
            updatedCount,
            resolvedCount,
            emailsSent,
            emailFailures,
            skipped.Count);

        return new NotificationDispatchResult(
            created.Count, updatedCount, resolvedCount, emailsSent, emailFailures, skipped);
    }

    /// <summary>
    /// Sends one digest per recipient covering the notifications created by this
    /// run, and records the outcome on those rows.
    ///
    /// Only newly created notifications are emailed: re-sending an unchanged
    /// condition every quarter of an hour is exactly the noise the reconciliation
    /// exists to prevent.
    ///
    /// A delivery failure does <b>not</b> throw. The default Hangfire retry would
    /// re-run the dispatch, find nothing new (the rows are already persisted) and
    /// therefore never re-attempt the email — the message would be lost rather than
    /// retried. It is logged at Error and recorded per notification instead, so a
    /// broken address stays visible without taking the whole farm's dispatch down
    /// with it.
    /// </summary>
    private async Task<(int Sent, int Failed)> DeliverDigestsAsync(
        Guid farmId,
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<NotificationPreference> preferences,
        IReadOnlyList<Notification> created,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (created.Count == 0)
        {
            return (0, 0);
        }

        var farmName = await _db.Farms
            .AsNoTracking()
            .Where(f => f.Id == farmId)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your farm";

        var sent = 0;
        var failed = 0;

        foreach (var group in created.GroupBy(n => n.UserId))
        {
            var emailable = group
                .Where(n => IsEmailEnabled(preferences, group.Key, n.AlertType))
                .ToList();

            if (emailable.Count == 0)
            {
                continue;
            }

            var email = members.FirstOrDefault(m => m.UserId == group.Key)?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                // A member with no email address simply has in-app only.
                continue;
            }

            var ordered = emailable
                .OrderBy(n => NotificationSeverity.Rank(n.Severity))
                .ThenBy(n => n.Title, StringComparer.Ordinal)
                .ToList();

            var items = BuildDigestItems(ordered);

            foreach (var notification in ordered)
            {
                notification.DeliveryAttempts++;
            }

            try
            {
                await _emailService.SendAlertNotificationsEmailAsync(email, farmName, items);

                sent++;
                foreach (var notification in ordered)
                {
                    notification.DeliveredAtUtc = now;
                    notification.LastDeliveryError = null;
                    notification.ModifiedAt = now;
                }
            }
            catch (Exception ex)
            {
                failed++;
                var message = $"{ex.GetType().Name}: {ex.Message}";
                foreach (var notification in ordered)
                {
                    notification.LastDeliveryError = Truncate(message, 1000);
                    notification.ModifiedAt = now;
                }

                _logger.LogError(ex,
                    "Job {JobName} failed to deliver the notification digest for user {UserId} on farm {FarmId} "
                    + "({AlertCount} alert(s)); the in-app notifications were persisted.",
                    JobNames.NotificationDispatch, group.Key, farmId, ordered.Count);
            }
        }

        if (sent > 0 || failed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return (sent, failed);
    }

    /// <summary>
    /// Caps how much a digest lists, so a farm with hundreds of overdue vaccines
    /// produces a readable message rather than a wall of text.
    /// </summary>
    private IReadOnlyList<NotificationEmailItem> BuildDigestItems(List<Notification> ordered)
    {
        var limit = Math.Max(1, _options.MaxAlertsPerEmail);
        var items = ordered
            .Take(limit)
            .Select(n => new NotificationEmailItem(n.Severity, n.Title, n.Message, n.Link))
            .ToList();

        var omitted = ordered.Count - items.Count;
        if (omitted > 0)
        {
            // Says so rather than truncating silently: a digest that just stops is
            // indistinguishable from a farm that only had that many problems.
            items.Add(new NotificationEmailItem(
                NotificationSeverity.Info,
                $"…and {omitted} more",
                $"{omitted} further alert(s) are not listed in this digest. Open the notification center for the full list.",
                null));
        }

        return items;
    }

    private static bool IsInAppEnabled(
        IReadOnlyList<NotificationPreference> preferences, Guid userId, string alertType)
    {
        var preference = preferences.FirstOrDefault(p =>
            p.UserId == userId && string.Equals(p.AlertType, alertType, StringComparison.Ordinal));

        // No row means "use the defaults", so a user who never opens the
        // preferences screen keeps receiving in-app notifications.
        return preference?.InAppEnabled ?? true;
    }

    private bool IsEmailEnabled(
        IReadOnlyList<NotificationPreference> preferences, Guid userId, string alertType)
    {
        var preference = preferences.FirstOrDefault(p =>
            p.UserId == userId && string.Equals(p.AlertType, alertType, StringComparison.Ordinal));

        return preference?.EmailEnabled
            ?? NotificationSeverity.IsEmailEnabledByDefault(
                NotificationAlertTypes.DefaultSeverityFor(alertType),
                _options.EmailMinSeverityOnByDefault);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private InvalidOperationException Fail(Guid farmId, string? code, string? message)
    {
        // Logged then thrown: a dispatch that could not evaluate the farm's
        // conditions must reach the scheduler (and the job status view) rather than
        // look like a farm with nothing to report.
        _logger.LogError(
            "Job {JobName} failed for farm {FarmId} while computing alert conditions: {ErrorCode} - {ErrorMessage}",
            JobNames.NotificationDispatch, farmId, code, message);

        return new InvalidOperationException(
            $"Job {JobNames.NotificationDispatch} failed for farm {farmId} while computing alert conditions: {code} - {message}");
    }
}
