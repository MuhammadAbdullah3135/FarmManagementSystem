namespace FMS.Application.Notifications;

/// <summary>
/// Notification configuration, bound from the <c>Notifications</c> configuration
/// section.
/// </summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Alert types included in a recipient's email digest by default.
    ///
    /// Critical-only by default: emailing every low-stock warning is how the
    /// channel gets ignored, and a user who wants more can opt in per alert type.
    /// </summary>
    public string EmailMinSeverityOnByDefault { get; set; } = NotificationSeverity.Critical;

    /// <summary>
    /// Maximum alerts listed in one digest email. Beyond this the digest says how
    /// many more there were, so a farm with hundreds of overdue vaccines does not
    /// produce a wall of text per recipient.
    /// </summary>
    public int MaxAlertsPerEmail { get; set; } = 20;
}
