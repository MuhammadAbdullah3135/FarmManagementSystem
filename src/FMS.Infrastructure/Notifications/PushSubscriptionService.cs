using FMS.Application.Common;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Notifications;

/// <inheritdoc />
public class PushSubscriptionService : IPushSubscriptionService
{
    private const int EndpointMaxLength = 500;
    private const int LabelMaxLength = 200;

    private readonly FmsDbContext _db;
    private readonly IPushSender _sender;
    private readonly NotificationOptions _notificationOptions;
    private readonly PushOptions _pushOptions;
    private readonly ILogger<PushSubscriptionService> _logger;

    public PushSubscriptionService(
        FmsDbContext db,
        IPushSender sender,
        IOptions<NotificationOptions> notificationOptions,
        IOptions<PushOptions> pushOptions,
        ILogger<PushSubscriptionService> logger)
    {
        _db = db;
        _sender = sender;
        _notificationOptions = notificationOptions.Value;
        _pushOptions = pushOptions.Value;
        _logger = logger;
    }

    public async Task<Result<PushSettingsDto>> GetSettingsAsync(Guid farmId, Guid userId)
    {
        var subscriptions = await _db.PushSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToListAsync();

        var configured = _sender.IsConfigured;

        return Result<PushSettingsDto>.Success(new PushSettingsDto
        {
            Enabled = configured,
            // Only handed out when the deployment can actually deliver: a browser that
            // subscribed against a keyless server would hold a subscription nothing ever
            // uses, and would show a permission prompt for it.
            VapidPublicKey = configured ? _pushOptions.PublicKey : null,
            MaxSubscriptionsPerUser = Math.Max(1, _pushOptions.MaxSubscriptionsPerUser),
            MinSeverityOnByDefault = _notificationOptions.PushMinSeverityOnByDefault,
            Subscriptions = subscriptions.Select(Map).ToList()
        });
    }

    public async Task<Result<PushSubscriptionDto>> RegisterAsync(
        Guid farmId,
        Guid userId,
        RegisterPushSubscriptionRequest request)
    {
        if (!_sender.IsConfigured)
        {
            // Refused rather than stored: a device registered against a deployment that
            // cannot push would sit in the user's list looking healthy while every alert
            // went nowhere.
            return Result<PushSubscriptionDto>.Validation(
                "Push notifications are not configured on this server.");
        }

        var validation = Validate(request);
        if (validation is not null)
        {
            return Result<PushSubscriptionDto>.Validation(validation);
        }

        var endpoint = request.Endpoint.Trim();
        var p256dh = request.P256dh.Trim();
        var auth = request.Auth.Trim();
        var label = NormalizeLabel(request.DeviceLabel);

        // Looked up by endpoint alone, deliberately: the same browser signing in as
        // somebody else must move the row, or the previous person would keep receiving
        // alerts on a device they no longer use — and the unique index would refuse the
        // new registration anyway.
        var existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint);

        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                _logger.LogInformation(
                    "A push subscription moved to another user on farm {FarmId}: the same browser registered again.",
                    farmId);

                existing.UserId = userId;
            }

            // Refreshed in place: a re-subscribe mints new keys for the same endpoint, and
            // keeping the old ones would encrypt for a key the browser no longer holds.
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.DeviceLabel = label ?? existing.DeviceLabel;
            existing.LastSeenAtUtc = now;
            existing.DisabledAtUtc = null;
            existing.LastFailureReason = null;
            existing.FailureCount = 0;
            existing.ModifiedAt = now;
            existing.ModifiedBy = userId;

            await _db.SaveChangesAsync();

            return Result<PushSubscriptionDto>.Success(Map(existing));
        }

        var limit = Math.Max(1, _pushOptions.MaxSubscriptionsPerUser);
        var active = await _db.PushSubscriptions
            .CountAsync(s => s.UserId == userId && s.DisabledAtUtc == null);

        if (active >= limit)
        {
            return Result<PushSubscriptionDto>.Validation(
                $"This account already has {limit} devices receiving push notifications. "
                + "Turn one of them off first.");
        }

        var subscription = new PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            DeviceLabel = label,
            LastSeenAtUtc = now,
            CreatedAt = now,
            CreatedBy = userId
        };

        _db.PushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        return Result<PushSubscriptionDto>.Success(Map(subscription));
    }

    public async Task<Result> UnregisterAsync(Guid farmId, Guid userId, Guid subscriptionId)
    {
        // Scoped to the caller, so an id from the request can never reach another
        // person's device — and the same answer for "does not exist" and "not yours",
        // matching how notifications behave.
        var subscription = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId);

        if (subscription is null)
        {
            return Result.NotFound("Push subscription not found");
        }

        // Deleted rather than disabled: this one is a deliberate act by the user, not a
        // push service reporting a browser gone. Keeping it would leave a device in their
        // list that they just removed.
        _db.PushSubscriptions.Remove(subscription);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Push subscription {SubscriptionId} removed by its owner on farm {FarmId}.",
            subscriptionId, farmId);

        return Result.Success();
    }

    /// <summary>
    /// What a usable subscription looks like.
    ///
    /// <para>
    /// Checked here rather than left to the first delivery because these values are
    /// encrypted against at send time: a truncated key would fail per alert, per device,
    /// in a scheduled job, and the only symptom a user would see is a phone that never
    /// buzzes. The endpoint must be https — RFC 8030 §5 requires it, and a stored
    /// <c>http:</c> endpoint is either a mistake or somebody's idea of a relay.
    /// </para>
    /// </summary>
    private static string? Validate(RegisterPushSubscriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return "A push subscription needs an endpoint.";
        }

        if (request.Endpoint.Trim().Length > EndpointMaxLength)
        {
            return $"A push subscription endpoint may not exceed {EndpointMaxLength} characters.";
        }

        if (!Uri.TryCreate(request.Endpoint.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return "A push subscription endpoint must be an https URL.";
        }

        if (!IsPublicPoint(request.P256dh))
        {
            return "The subscription's p256dh key is not a base64url uncompressed P-256 point.";
        }

        if (!IsAuthSecret(request.Auth))
        {
            return "The subscription's auth secret is not 16 base64url octets.";
        }

        return null;
    }

    /// <summary>
    /// A subscription key is stored to be encrypted against, so it is checked for the two
    /// things that make that possible at all: the uncompressed point form, and the curve
    /// equation. A key that fails either is a browser bug or a forged registration, and it is
    /// better refused here than discovered as a per-alert encryption failure inside a job.
    /// </summary>
    private static bool IsPublicPoint(string? value) =>
        Base64Url.TryDecode(value?.Trim(), VapidKeys.PublicKeyLength, out var point)
        && P256.IsOnCurve(point);

    private static bool IsAuthSecret(string? value) =>
        Base64Url.TryDecode(value?.Trim(), 16, out _);

    private static string? NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var trimmed = label.Trim();
        return trimmed.Length <= LabelMaxLength ? trimmed : trimmed[..LabelMaxLength];
    }

    private static PushSubscriptionDto Map(PushSubscription subscription) => new()
    {
        Id = subscription.Id,
        DeviceLabel = subscription.DeviceLabel,
        CreatedAt = subscription.CreatedAt,
        LastSeenAtUtc = subscription.LastSeenAtUtc,
        IsActive = subscription.IsActive,
        LastFailureReason = subscription.LastFailureReason
    };
}
