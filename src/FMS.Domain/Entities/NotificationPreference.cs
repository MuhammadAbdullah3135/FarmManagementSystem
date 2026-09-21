using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// One recipient's delivery choices for one alert type on one farm.
///
/// Deliberately sparse: <em>the absence of a row means "use the defaults"</em>,
/// so adding an alert type or a user never requires a backfill, and a user who
/// never opens the preferences screen keeps behaving exactly as designed.
///
/// Preferences exist to stop the notification system becoming noise — a user who
/// is not responsible for breeding, or who does not want an email for every
/// low-stock warning, says so once per alert type instead of being trained to
/// ignore the whole feature.
///
/// Bookkeeping rather than business data: excluded from the audit log for the
/// same reason as <see cref="Notification"/>.
/// </summary>
public class NotificationPreference : AuditableEntity, IAuditLogExcluded
{
    public Guid FarmId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>One of the dashboard's alert types (see <c>NotificationAlertTypes</c>).</summary>
    public string AlertType { get; set; } = string.Empty;

    /// <summary>Whether a notification row is created for this recipient at all.</summary>
    public bool InAppEnabled { get; set; } = true;

    /// <summary>Whether this alert type is included in the recipient's email digest.</summary>
    public bool EmailEnabled { get; set; }

    public Farm Farm { get; set; } = null!;

    public User User { get; set; } = null!;
}
