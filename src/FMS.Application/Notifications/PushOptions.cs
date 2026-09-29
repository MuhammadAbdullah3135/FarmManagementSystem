namespace FMS.Application.Notifications;

/// <summary>
/// Push transport configuration, bound from the <c>Push</c> section.
///
/// <para>
/// <see cref="Enabled"/> defaults to <c>false</c> and every key is empty, so
/// deploying this changes nothing until an operator supplies VAPID keys — the
/// same shape as <c>EmailOptions</c> defaulting to the Log transport. A checkout
/// with no keys runs, serves and passes its tests; it simply has no push channel,
/// and the preferences screen says so instead of offering a switch that cannot
/// work.
/// </para>
///
/// <para>
/// This section holds the *transport* only. Which alerts are worth an out-of-app
/// channel is policy and lives in <see cref="NotificationOptions"/>, next to the
/// email threshold it mirrors.
/// </para>
///
/// <para>
/// The VAPID private key is a credential. It is never logged and never committed:
/// <c>PushConfigurationGuard</c> refuses to start outside Development when push is
/// enabled but unusable, so a broken configuration cannot silently deliver nothing.
/// </para>
/// </summary>
public class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>
    /// Whether the push channel is on at all. Off by default, so a deployment
    /// without keys behaves exactly as it did before this feature existed.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The VAPID <c>sub</c> claim: who to contact about this deployment's push
    /// traffic. A <c>mailto:</c> address or an <c>https:</c> URL, as RFC 8292
    /// requires — push services reject a JWT without one.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// The VAPID public key, base64url uncompressed P-256 point (65 octets). This
    /// is the half that is handed to browsers as <c>applicationServerKey</c>, so it
    /// is served to the client by <c>GET …/notifications/push</c>.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Signs the VAPID JWT. A credential: never logged, never committed.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// How long the push service may keep trying to deliver, in seconds.
    ///
    /// Four hours: long enough to survive a phone that is off for the afternoon —
    /// which is the normal case for a farm — and short enough that a notification
    /// about a measurement due today does not arrive tomorrow.
    /// </summary>
    public int TtlSeconds { get; set; } = 4 * 60 * 60;

    /// <summary>
    /// Devices one user may have subscribed at once. A cap exists so a stuck client
    /// that re-subscribes on every visit cannot grow the table without bound; five
    /// is past the point of a person's actual devices.
    /// </summary>
    public int MaxSubscriptionsPerUser { get; set; } = 5;

    /// <summary>
    /// Per-attempt HTTP timeout. Ten seconds rather than the email transport's five:
    /// a push service is a fan-out edge and answers slower than a mail API, and this
    /// call happens inside a scheduled job rather than inside a user's request.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>True when both halves of the VAPID pair are present.</summary>
    public bool HasKeys =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
