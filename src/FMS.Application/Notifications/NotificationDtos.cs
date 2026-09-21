namespace FMS.Application.Notifications;

// ── Notification ────────────────────────────────────────

public class NotificationDto
{
    public Guid Id { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public bool IsDismissed { get; set; }
    public bool IsResolved { get; set; }

    /// <summary>When an out-of-app channel accepted it, or null for in-app only.</summary>
    public DateTime? DeliveredAtUtc { get; set; }
}

public class NotificationListDto
{
    public List<NotificationDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    /// <summary>Unread, undismissed, unresolved count — what the header badge shows.</summary>
    public int UnreadCount { get; set; }
}

/// <summary>Filters for the notification list. Dismissed and resolved rows are opt-in.</summary>
public class NotificationQuery
{
    public bool UnreadOnly { get; set; }
    public bool IncludeDismissed { get; set; }
    public bool IncludeResolved { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

// ── Preferences ─────────────────────────────────────────

public class NotificationPreferenceDto
{
    public string AlertType { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
}

/// <summary>
/// The preferences screen's whole payload.
///
/// Ships the defaults alongside the recipient's overrides so the UI renders the
/// matrix from the server's vocabulary instead of hardcoding alert types the API
/// could add.
/// </summary>
public class NotificationPreferenceSettingsDto
{
    public List<NotificationPreferenceDto> Preferences { get; set; } = new();

    /// <summary>Channel names in display order; the preference row is one flag per name.</summary>
    public List<string> Channels { get; set; } = new() { NotificationChannels.InApp, NotificationChannels.Email };

    public string EmailMinSeverityOnByDefault { get; set; } = NotificationSeverity.Critical;
}

public class NotificationPreferenceUpdateDto
{
    public string AlertType { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
}

public class UpdateNotificationPreferencesRequest
{
    public List<NotificationPreferenceUpdateDto> Preferences { get; set; } = new();
}

/// <summary>
/// The delivery channels. In-app (the notification center) and email are
/// implemented; push and SMS are deliberately not, and adding either is a new
/// value here plus a new sender — the per-(user, farm, alert type) preference
/// model already has a slot for it.
/// </summary>
public static class NotificationChannels
{
    public const string InApp = "InApp";
    public const string Email = "Email";
}
