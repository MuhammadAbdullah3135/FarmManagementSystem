using FMS.Application.Common;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Notifications;

/// <inheritdoc />
public class NotificationService : INotificationService
{
    private readonly FmsDbContext _db;
    private readonly NotificationOptions _options;

    public NotificationService(FmsDbContext db, IOptions<NotificationOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<Result<NotificationListDto>> GetAsync(Guid farmId, Guid userId, NotificationQuery query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var baseQuery = QueryFor(farmId, userId)
            .Where(n => query.IncludeDismissed || n.DismissedAtUtc == null)
            .Where(n => query.IncludeResolved || n.ResolvedAtUtc == null);

        if (query.UnreadOnly)
        {
            baseQuery = baseQuery.Where(n => n.ReadAtUtc == null);
        }

        var totalCount = await baseQuery.CountAsync();

        var items = await baseQuery
            // Most severe first so a page boundary cannot hide the critical ones,
            // then newest — the same ordering the dashboard uses.
            .OrderBy(n => n.Severity == NotificationSeverity.Critical ? 0
                : n.Severity == NotificationSeverity.Warning ? 1 : 2)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<NotificationListDto>.Success(new NotificationListDto
        {
            Items = items.Select(Map).ToList(),
            TotalCount = totalCount,
            UnreadCount = await CountUnreadAsync(farmId, userId)
        });
    }

    public async Task<Result<int>> GetUnreadCountAsync(Guid farmId, Guid userId)
    {
        return Result<int>.Success(await CountUnreadAsync(farmId, userId));
    }

    public async Task<Result<NotificationDto>> MarkReadAsync(Guid farmId, Guid userId, Guid notificationId)
    {
        var notification = await QueryFor(farmId, userId).FirstOrDefaultAsync(n => n.Id == notificationId);
        if (notification is null)
        {
            // Deliberately the same answer for "does not exist" and "belongs to
            // somebody else": a notification's existence is not the caller's
            // business, and the recipient filter above is what makes it private.
            return Result<NotificationDto>.NotFound("Notification not found");
        }

        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = DateTime.UtcNow;
            notification.ModifiedAt = notification.ReadAtUtc;
            await _db.SaveChangesAsync();
        }

        return Result<NotificationDto>.Success(Map(notification));
    }

    public async Task<Result<int>> MarkAllReadAsync(Guid farmId, Guid userId)
    {
        // Includes resolved rows: leaving an unread row behind after "mark all
        // read" would let the badge come back for something already dealt with.
        var unread = await QueryFor(farmId, userId)
            .Where(n => n.ReadAtUtc == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
            notification.ModifiedAt = now;
        }

        if (unread.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Result<int>.Success(unread.Count);
    }

    public async Task<Result<NotificationDto>> DismissAsync(Guid farmId, Guid userId, Guid notificationId)
    {
        var notification = await QueryFor(farmId, userId).FirstOrDefaultAsync(n => n.Id == notificationId);
        if (notification is null)
        {
            return Result<NotificationDto>.NotFound("Notification not found");
        }

        if (notification.DismissedAtUtc is null)
        {
            var now = DateTime.UtcNow;
            // Dismissing is an acknowledgement: it clears the unread badge too.
            notification.DismissedAtUtc = now;
            notification.ReadAtUtc ??= now;
            notification.ModifiedAt = now;
            await _db.SaveChangesAsync();
        }

        return Result<NotificationDto>.Success(Map(notification));
    }

    public async Task<Result<NotificationPreferenceSettingsDto>> GetPreferencesAsync(Guid farmId, Guid userId)
    {
        var stored = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.FarmId == farmId && p.UserId == userId)
            .ToListAsync();

        return Result<NotificationPreferenceSettingsDto>.Success(BuildSettings(stored));
    }

    public async Task<Result<NotificationPreferenceSettingsDto>> UpdatePreferencesAsync(
        Guid farmId,
        Guid userId,
        UpdateNotificationPreferencesRequest request)
    {
        var unknown = request.Preferences
            .Select(p => p.AlertType)
            .Where(alertType => !NotificationAlertTypes.IsKnown(alertType))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknown.Count > 0)
        {
            // Refused rather than ignored: silently dropping an unknown row would
            // leave the caller believing a channel was turned off when it was
            // never understood in the first place.
            return Result<NotificationPreferenceSettingsDto>.Validation(
                $"Unknown alert type(s): {string.Join(", ", unknown)}");
        }

        var stored = await _db.NotificationPreferences
            .Where(p => p.FarmId == farmId && p.UserId == userId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var update in request.Preferences)
        {
            var existing = stored.FirstOrDefault(p =>
                string.Equals(p.AlertType, update.AlertType, StringComparison.Ordinal));

            if (existing is null)
            {
                _db.NotificationPreferences.Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    UserId = userId,
                    AlertType = update.AlertType,
                    InAppEnabled = update.InAppEnabled,
                    EmailEnabled = update.EmailEnabled,
                    CreatedAt = now,
                    CreatedBy = userId
                });
            }
            else
            {
                existing.InAppEnabled = update.InAppEnabled;
                existing.EmailEnabled = update.EmailEnabled;
                existing.ModifiedAt = now;
                existing.ModifiedBy = userId;
            }
        }

        await _db.SaveChangesAsync();

        var refreshed = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.FarmId == farmId && p.UserId == userId)
            .ToListAsync();

        return Result<NotificationPreferenceSettingsDto>.Success(BuildSettings(refreshed));
    }

    /// <summary>
    /// The caller's own notifications only. Tracked (not <c>AsNoTracking</c>) because
    /// every caller either reads them for a response or mutates them.
    /// </summary>
    private IQueryable<Notification> QueryFor(Guid farmId, Guid userId) =>
        _db.Notifications.Where(n => n.FarmId == farmId && n.UserId == userId);

    private Task<int> CountUnreadAsync(Guid farmId, Guid userId) =>
        _db.Notifications.CountAsync(n =>
            n.FarmId == farmId
            && n.UserId == userId
            && n.ReadAtUtc == null
            && n.DismissedAtUtc == null
            // A condition that cleared itself is what the badge should stop
            // nagging about: there is nothing left to act on.
            && n.ResolvedAtUtc == null);

    /// <summary>
    /// The full matrix: every known alert type, with the stored override where one
    /// exists and the computed default where it does not.
    /// </summary>
    private NotificationPreferenceSettingsDto BuildSettings(IReadOnlyList<NotificationPreference> stored) =>
        new()
        {
            Preferences = NotificationAlertTypes.All.Select(alertType =>
            {
                var preference = stored.FirstOrDefault(p =>
                    string.Equals(p.AlertType, alertType, StringComparison.Ordinal));

                return new NotificationPreferenceDto
                {
                    AlertType = alertType,
                    InAppEnabled = preference?.InAppEnabled ?? true,
                    EmailEnabled = preference?.EmailEnabled
                        ?? NotificationSeverity.IsEmailEnabledByDefault(
                            NotificationAlertTypes.DefaultSeverityFor(alertType),
                            _options.EmailMinSeverityOnByDefault)
                };
            }).ToList(),
            Channels = new List<string> { NotificationChannels.InApp, NotificationChannels.Email },
            EmailMinSeverityOnByDefault = _options.EmailMinSeverityOnByDefault
        };

    private static NotificationDto Map(Notification n) => new()
    {
        Id = n.Id,
        AlertType = n.AlertType,
        Severity = n.Severity,
        Title = n.Title,
        Message = n.Message,
        Link = n.Link,
        DueDate = n.DueDate,
        CreatedAt = n.CreatedAt,
        IsRead = n.ReadAtUtc is not null,
        ReadAtUtc = n.ReadAtUtc,
        IsDismissed = n.DismissedAtUtc is not null,
        IsResolved = n.ResolvedAtUtc is not null,
        DeliveredAtUtc = n.DeliveredAtUtc
    };
}
