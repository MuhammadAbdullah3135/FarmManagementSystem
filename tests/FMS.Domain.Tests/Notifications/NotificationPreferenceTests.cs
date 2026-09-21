using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Notifications;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Notifications;

/// <summary>
/// Preferences exist so the notification system does not become noise. The
/// important properties are that they are sparse (no row = defaults) and that the
/// defaults the screen shows are exactly the ones the dispatcher applies.
/// </summary>
public class NotificationPreferenceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NotificationService CreateService(FmsDbContext context, string emailMinSeverity = "Critical") =>
        new(context, Options.Create(new NotificationOptions
        {
            EmailMinSeverityOnByDefault = emailMinSeverity
        }));

    [Fact]
    public async Task GetPreferencesAsync_WithNothingStored_ReturnsADefaultForEveryAlertType()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.GetPreferencesAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess);
        var preferences = result.Value!.Preferences;

        // The matrix is complete, so the UI never has to invent a row.
        Assert.Equal(NotificationAlertTypes.All.Count, preferences.Count);
        Assert.Equal(
            NotificationAlertTypes.All.OrderBy(t => t, StringComparer.Ordinal),
            preferences.Select(p => p.AlertType).OrderBy(t => t, StringComparer.Ordinal));

        // In-app on for everything: a silent notification center is a broken one.
        Assert.All(preferences, p => Assert.True(p.InAppEnabled));

        // Email on only where the alert type's declared severity is at or above
        // the configured threshold — Critical by default.
        Assert.True(preferences.Single(p => p.AlertType == NotificationAlertTypes.OverdueVaccination).EmailEnabled);
        Assert.True(preferences.Single(p => p.AlertType == NotificationAlertTypes.OverdueTask).EmailEnabled);
        Assert.False(preferences.Single(p => p.AlertType == NotificationAlertTypes.OverdueWeightCheck).EmailEnabled);
        Assert.False(preferences.Single(p => p.AlertType == NotificationAlertTypes.Medicine).EmailEnabled);
        Assert.False(preferences.Single(p => p.AlertType == NotificationAlertTypes.DueBirth).EmailEnabled);
        Assert.False(preferences.Single(p => p.AlertType == NotificationAlertTypes.LowInventory).EmailEnabled);

        Assert.Equal(
            new[] { NotificationChannels.InApp, NotificationChannels.Email },
            result.Value.Channels);
    }

    [Fact]
    public async Task GetPreferencesAsync_ThresholdOfWarning_TurnsEmailOnForWarningsToo()
    {
        using var context = CreateContext();
        var service = CreateService(context, NotificationSeverity.Warning);

        var result = await service.GetPreferencesAsync(Guid.NewGuid(), Guid.NewGuid());

        // Deployment-level policy, applied consistently because the default is
        // derived from one place.
        Assert.True(result.Value!.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.OverdueWeightCheck).EmailEnabled);
        Assert.False(result.Value.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.LowInventory).EmailEnabled);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_UpsertsOnlyWhatWasSent()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var update = await service.UpdatePreferencesAsync(farmId, userId, new UpdateNotificationPreferencesRequest
        {
            Preferences =
            [
                new NotificationPreferenceUpdateDto
                {
                    AlertType = NotificationAlertTypes.OverdueVaccination,
                    InAppEnabled = true,
                    EmailEnabled = false
                }
            ]
        });

        Assert.True(update.IsSuccess);

        // Only the overridden type got a row...
        var stored = await context.NotificationPreferences.ToListAsync();
        Assert.Single(stored);
        Assert.Equal(NotificationAlertTypes.OverdueVaccination, stored[0].AlertType);
        Assert.False(stored[0].EmailEnabled);
        Assert.Equal(userId, stored[0].UserId);
        Assert.Equal(userId, stored[0].CreatedBy);

        // ...and the returned matrix reflects the override while every other type
        // keeps its default.
        var preferences = update.Value!.Preferences;
        Assert.False(preferences.Single(p => p.AlertType == NotificationAlertTypes.OverdueVaccination).EmailEnabled);
        Assert.True(preferences.Single(p => p.AlertType == NotificationAlertTypes.OverdueTask).EmailEnabled);

        // Updating again overwrites rather than adding a second row.
        await service.UpdatePreferencesAsync(farmId, userId, new UpdateNotificationPreferencesRequest
        {
            Preferences =
            [
                new NotificationPreferenceUpdateDto
                {
                    AlertType = NotificationAlertTypes.OverdueVaccination,
                    InAppEnabled = false,
                    EmailEnabled = true
                }
            ]
        });

        stored = await context.NotificationPreferences.ToListAsync();
        Assert.Single(stored);
        Assert.False(stored[0].InAppEnabled);
        Assert.True(stored[0].EmailEnabled);
        Assert.NotNull(stored[0].ModifiedAt);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_UnknownAlertType_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.UpdatePreferencesAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateNotificationPreferencesRequest
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateDto { AlertType = "SomethingElse", EmailEnabled = true }
                ]
            });

        // Refused rather than ignored: silently dropping it would leave the caller
        // believing a channel had been turned off.
        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Contains("SomethingElse", result.Error.Message);
        Assert.Empty(await context.NotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task Preferences_AreScopedToTheUserAndFarm()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmA = Guid.NewGuid();
        var farmB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await service.UpdatePreferencesAsync(farmA, userA, new UpdateNotificationPreferencesRequest
        {
            Preferences =
            [
                new NotificationPreferenceUpdateDto { AlertType = NotificationAlertTypes.DueBirth, EmailEnabled = true }
            ]
        });

        // Another user on the same farm, and the same user on another farm, both
        // still see the defaults.
        var otherUser = await service.GetPreferencesAsync(farmA, userB);
        var otherFarm = await service.GetPreferencesAsync(farmB, userA);

        Assert.False(otherUser.Value!.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.DueBirth).EmailEnabled);
        Assert.False(otherFarm.Value!.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.DueBirth).EmailEnabled);
    }

    // ── Reading and acknowledging ───────────────────────────

    private static Notification NewNotification(
        Guid farmId, Guid userId, string title, DateTime? readAt = null, DateTime? resolvedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        UserId = userId,
        AlertType = NotificationAlertTypes.OverdueVaccination,
        Severity = NotificationSeverity.Critical,
        SourceKey = $"{NotificationAlertTypes.OverdueVaccination}:{Guid.NewGuid()}",
        Title = title,
        Message = "message",
        CreatedAt = DateTime.UtcNow,
        ReadAtUtc = readAt,
        ResolvedAtUtc = resolvedAt
    };

    [Fact]
    public async Task GetAsync_ReturnsOnlyTheCallersOwnNotifications()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        context.Notifications.AddRange(
            NewNotification(farmId, mine, "mine"),
            NewNotification(farmId, theirs, "theirs"));
        await context.SaveChangesAsync();

        var result = await service.GetAsync(farmId, mine, new NotificationQuery());

        Assert.Single(result.Value!.Items);
        Assert.Equal("mine", result.Value.Items[0].Title);
        Assert.Equal(1, result.Value.UnreadCount);
    }

    [Fact]
    public async Task GetAsync_DefaultFilters_HideDismissedAndResolved()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var open = NewNotification(farmId, userId, "open");
        var resolved = NewNotification(farmId, userId, "resolved", resolvedAt: DateTime.UtcNow);
        var dismissed = NewNotification(farmId, userId, "dismissed");
        dismissed.DismissedAtUtc = DateTime.UtcNow;
        context.Notifications.AddRange(open, resolved, dismissed);
        await context.SaveChangesAsync();

        var defaultView = await service.GetAsync(farmId, userId, new NotificationQuery());
        Assert.Single(defaultView.Value!.Items);
        Assert.Equal("open", defaultView.Value.Items[0].Title);
        // The badge counts only what is still actionable.
        Assert.Equal(1, defaultView.Value.UnreadCount);

        var everything = await service.GetAsync(farmId, userId, new NotificationQuery
        {
            IncludeDismissed = true,
            IncludeResolved = true
        });
        Assert.Equal(3, everything.Value!.Items.Count);
        Assert.Equal(3, everything.Value.TotalCount);
    }

    [Fact]
    public async Task MarkRead_IsIdempotentAndRecordsWhenItHappened()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var notification = NewNotification(farmId, userId, "read me");
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        var first = await service.MarkReadAsync(farmId, userId, notification.Id);
        var second = await service.MarkReadAsync(farmId, userId, notification.Id);

        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.IsRead);
        Assert.NotNull(first.Value.ReadAtUtc);
        // Re-reading does not move the original timestamp.
        Assert.Equal(first.Value.ReadAtUtc, second.Value!.ReadAtUtc);
        Assert.Equal(0, (await service.GetUnreadCountAsync(farmId, userId)).Value);
    }

    [Fact]
    public async Task MarkRead_OnAnotherUsersNotification_IsNotFoundAndChangesNothing()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var notification = NewNotification(farmId, theirs, "not mine");
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        var result = await service.MarkReadAsync(farmId, mine, notification.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);

        var untouched = await context.Notifications.SingleAsync(n => n.Id == notification.Id);
        Assert.Null(untouched.ReadAtUtc);
    }

    [Fact]
    public async Task Dismiss_MarksItReadAndKeepsTheRow()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var notification = NewNotification(farmId, userId, "dismiss me");
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        var result = await service.DismissAsync(farmId, userId, notification.Id);

        Assert.True(result.Value!.IsDismissed);
        // Dismissing is an acknowledgement: the badge clears too.
        Assert.True(result.Value.IsRead);

        // Not a delete: the record survives so history is not rewritten.
        var stored = await context.Notifications.SingleAsync(n => n.Id == notification.Id);
        Assert.NotNull(stored.DismissedAtUtc);
    }

    [Fact]
    public async Task MarkAllRead_ClearsEveryUnreadRowForTheCallerOnly()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        context.Notifications.AddRange(
            NewNotification(farmId, mine, "one"),
            NewNotification(farmId, mine, "two"),
            NewNotification(farmId, theirs, "not mine"));
        await context.SaveChangesAsync();

        var result = await service.MarkAllReadAsync(farmId, mine);

        Assert.Equal(2, result.Value);
        Assert.Equal(0, (await service.GetUnreadCountAsync(farmId, mine)).Value);
        // The other member's unread state is untouched.
        Assert.Equal(1, (await service.GetUnreadCountAsync(farmId, theirs)).Value);
    }
}
