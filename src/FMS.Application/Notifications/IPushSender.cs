namespace FMS.Application.Notifications;

/// <summary>
/// One device to deliver to, as the sender needs it: where to POST and the two
/// values that encrypt the body for it.
///
/// Carried as a plain record rather than the entity so the sender cannot touch —
/// or accidentally save — the tracked row, and so a retry can be handed the same
/// target without re-reading the database.
/// </summary>
public sealed record PushSubscriptionTarget(
    Guid SubscriptionId,
    string Endpoint,
    string P256dh,
    string Auth);

/// <summary>
/// The message one push carries.
///
/// <para>
/// <see cref="AlertCount"/> is why there is one push per recipient per dispatch run
/// rather than one per notification: the job runs every fifteen minutes and a farm
/// can produce a dozen alerts at once (a medicine running out is rarely alone), and
/// a phone that buzzes twelve times is a phone whose notifications get switched off.
/// One push says "3 new alerts" and opens the notification centre.
/// </para>
///
/// <para>
/// The text is English, exactly as the email digest's is: a push is rendered by the
/// browser before any client of ours is running, so there is nobody to hand a message
/// key to. The row it refers to carries its key, and the app renders it in the
/// reader's language a moment later when they open the notification.
/// </para>
/// </summary>
public sealed record PushMessage(
    string Title,
    string Body,
    string? Url,
    string Tag,
    string Severity,
    int AlertCount);

/// <summary>
/// What one delivery attempt did.
///
/// <see cref="SubscriptionGone"/> is separated from a plain failure because the two
/// call for opposite responses: a gone subscription is the client's news (disable the
/// row and never try again), a failure is ours (count it, keep the row).
/// </summary>
public readonly record struct PushSendResult(bool Delivered, bool SubscriptionGone, string? FailureReason)
{
    /// <summary>The push service accepted the message.</summary>
    public static PushSendResult Sent { get; } = new(true, false, null);

    /// <summary>The subscription no longer exists (HTTP 404/410) and must be disabled.</summary>
    public static PushSendResult Gone(string reason) => new(false, true, reason);

    /// <summary>The attempt failed but the subscription may still work.</summary>
    public static PushSendResult Failed(string reason) => new(false, false, reason);
}

/// <summary>
/// Delivers one message to one Web Push subscription.
///
/// The seam between "the dispatcher decided somebody should be told" and "the
/// transport that carries it": tests substitute a recording sender to assert the push
/// channel's policy without cryptography or a network, and the real implementation
/// (<c>WebPushSender</c>) is the only place that knows about VAPID and RFC 8291.
/// </summary>
public interface IPushSender
{
    /// <summary>True when a deployment is configured to send at all.</summary>
    bool IsConfigured { get; }

    Task<PushSendResult> SendAsync(
        PushSubscriptionTarget target,
        PushMessage message,
        CancellationToken cancellationToken = default);
}
