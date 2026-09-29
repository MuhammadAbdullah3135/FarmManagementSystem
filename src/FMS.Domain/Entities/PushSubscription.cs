using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// One browser's push subscription, registered by the user signed in on it.
///
/// <para>
/// A row is a <em>device</em>, not a farm: a browser holds one subscription per
/// origin regardless of which farm is active, and a person who manages two farms
/// wants the alerts from both on the phone in their pocket. Which alerts reach a
/// device is decided per farm by <see cref="NotificationPreference"/>, which the
/// dispatcher already applies; this table only answers "where can we reach this
/// person".
/// </para>
///
/// <para>
/// The three values that make delivery possible — <see cref="Endpoint"/>,
/// <see cref="P256dh"/> and <see cref="Auth"/> — come from the browser's
/// <c>PushSubscription</c>. Only the endpoint identifies it; the keys are
/// regenerated when a browser re-subscribes, so an existing endpoint is refreshed
/// in place rather than inserted again.
/// </para>
///
/// <para>
/// Lifecycle: <see cref="DisabledAtUtc"/> is set when the push service reports the
/// subscription is gone (HTTP 404/410 — the browser was uninstalled, the profile
/// deleted, or permission revoked). The row is kept rather than deleted so a device
/// that quietly stopped receiving is diagnosable after the fact, and a later
/// registration of the same endpoint clears it.
/// </para>
///
/// <para>
/// Bookkeeping rather than user-authored business data: excluded from the audit
/// log, like <see cref="Notification"/> and <see cref="NotificationPreference"/>.
/// </para>
/// </summary>
public class PushSubscription : AuditableEntity, IAuditLogExcluded
{
    /// <summary>The person this device can reach.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The push service URL the browser minted. Opaque and potentially long, and
    /// unique across the deployment: the same browser signing in as somebody else
    /// moves the row to the new user instead of leaving the previous person's
    /// credential behind.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The browser's P-256 public key in base64url (the subscription's
    /// <c>keys.p256dh</c>): the half of the encryption that the receiver owns.
    /// </summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>
    /// The 16-octet authentication secret in base64url (<c>keys.auth</c>), mixed
    /// into the key derivation so a push service cannot forge a message the
    /// browser would accept.
    /// </summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>
    /// What the user will recognise in their own list of devices, derived from the
    /// browser's user agent at registration ("Chrome on Android"). Null for a client
    /// that did not send one.
    /// </summary>
    public string? DeviceLabel { get; set; }

    /// <summary>
    /// When this device last registered or was last seen delivering. Used to order
    /// the list a user sees, and as the freshness signal if a stale row ever has to
    /// be chosen between.
    /// </summary>
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>
    /// When the push service said this subscription no longer exists. Null means
    /// the device is still expected to receive.
    /// </summary>
    public DateTime? DisabledAtUtc { get; set; }

    /// <summary>Why it was disabled, or the last delivery failure, kept for diagnosis.</summary>
    public string? LastFailureReason { get; set; }

    /// <summary>Failed delivery attempts since the last success, so a flapping device is visible.</summary>
    public int FailureCount { get; set; }

    public User User { get; set; } = null!;

    /// <summary>True while deliveries should still be attempted to this device.</summary>
    public bool IsActive => DisabledAtUtc is null;
}
