namespace FMS.Application.Notifications;

/// <summary>
/// One of the caller's registered devices.
///
/// Deliberately without the endpoint or the encryption keys: they are credentials
/// the browser already holds, nothing in the UI needs them, and echoing a
/// capability URL back over the wire only widens where it can leak from.
/// </summary>
public class PushSubscriptionDto
{
    public Guid Id { get; set; }

    public string? DeviceLabel { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>False once the push service reported it gone; shown so a user can clean up.</summary>
    public bool IsActive { get; set; }

    public string? LastFailureReason { get; set; }
}

/// <summary>
/// A browser registering itself for push.
///
/// The three values are exactly what <c>PushSubscription.toJSON()</c> yields, so
/// the client forwards them rather than deriving anything.
/// </summary>
public class RegisterPushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The subscription's <c>keys.p256dh</c>, base64url.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>The subscription's <c>keys.auth</c>, base64url.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Optional human label for the device list.</summary>
    public string? DeviceLabel { get; set; }
}

/// <summary>
/// Everything the client needs to decide what to offer on the push card.
///
/// <see cref="Enabled"/> is the server's answer, not the browser's: a deployment
/// with no VAPID keys has no push channel, and the screen says so instead of
/// prompting for a permission that could never deliver anything.
/// </summary>
public class PushSettingsDto
{
    /// <summary>True when this deployment can send push at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The VAPID public key the browser must subscribe with, or null when push is
    /// off. Public by design — it is the half a browser is given.
    /// </summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>The ceiling on this user's devices, so the UI can explain a refusal.</summary>
    public int MaxSubscriptionsPerUser { get; set; }

    /// <summary>Severity at or above which an alert type is pushed by default.</summary>
    public string MinSeverityOnByDefault { get; set; } = NotificationSeverity.Critical;

    /// <summary>The caller's own devices, newest first.</summary>
    public List<PushSubscriptionDto> Subscriptions { get; set; } = new();
}
