using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// A persisted alert for one recipient on one farm.
///
/// Until now the dashboard's alerts were computed on every page load and thrown
/// away, so a condition that appeared and disappeared while nobody was looking
/// left no trace. These rows are the durable record: the
/// <c>notification-dispatch</c> job evaluates the same alert conditions the
/// dashboard does and reconciles a row per recipient.
///
/// <para>
/// <see cref="SourceKey"/> is the identity of the underlying condition, not of
/// the notification. It is built from entity ids only — never a date or a
/// severity — so a condition that merely *moves* (a vaccination due date pushed
/// back, a task rescheduled) refreshes the existing row instead of alerting
/// again. That is what keeps "new/changed" from becoming "repeated".
/// </para>
///
/// <para>
/// Lifecycle: <see cref="ReadAtUtc"/> is read/unread, <see cref="DismissedAtUtc"/>
/// is the recipient clearing it from their list (the row survives so history is
/// not rewritten), and <see cref="ResolvedAtUtc"/> is the condition going away.
/// Only <see cref="ResolvedAtUtc"/> frees the <see cref="SourceKey"/> to alert
/// again if the same condition later returns.
/// </para>
///
/// Internal alert bookkeeping rather than user-authored business data: excluded
/// from the audit log, because job-created rows have no HTTP principal to
/// attribute them to and marking one read would otherwise write an audit row per
/// action.
/// </summary>
public class Notification : AuditableEntity, IAuditLogExcluded
{
    public Guid FarmId { get; set; }

    /// <summary>The member this notification is for. Notifications are never shared.</summary>
    public Guid UserId { get; set; }

    /// <summary>One of the dashboard's alert types (see <c>NotificationAlertTypes</c>).</summary>
    public string AlertType { get; set; } = string.Empty;

    /// <summary>Critical, Warning or Info — mirrors the dashboard alert's severity.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Stable identity of the underlying condition, e.g.
    /// <c>OverdueVaccination:{animalId}:{vaccineTypeId}</c>. Unique per recipient
    /// while unresolved; that is what makes dispatch idempotent.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>In-app route the alert points at (<c>/dashboard/health/vaccinations</c>, …).</summary>
    public string? Link { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>When the recipient first read it. Null means unread.</summary>
    public DateTime? ReadAtUtc { get; set; }

    /// <summary>When the recipient dismissed it from their list. Null means not dismissed.</summary>
    public DateTime? DismissedAtUtc { get; set; }

    /// <summary>
    /// When the condition stopped being reported. A resolved notification is
    /// closed: it is never re-notified, and its <see cref="SourceKey"/> may be
    /// used again by a later occurrence.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>
    /// When at least one out-of-app channel accepted this notification (the
    /// email digest). Null means in-app only.
    /// </summary>
    public DateTime? DeliveredAtUtc { get; set; }

    /// <summary>Out-of-app delivery attempts, so a repeatedly failing channel is visible.</summary>
    public int DeliveryAttempts { get; set; }

    /// <summary>Last out-of-app delivery error, kept so a silent channel failure is diagnosable.</summary>
    public string? LastDeliveryError { get; set; }

    public Farm Farm { get; set; } = null!;

    public User User { get; set; } = null!;

    /// <summary>True while the condition is still present and the row is not closed.</summary>
    public bool IsOpen => ResolvedAtUtc is null;
}
