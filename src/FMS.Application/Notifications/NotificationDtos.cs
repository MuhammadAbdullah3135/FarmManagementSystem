namespace FMS.Application.Notifications;

// ── Notification ────────────────────────────────────────

public class NotificationDto
{
    public Guid Id { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    /// <summary>English, as dispatched: the client's fallback when it has no key.</summary>
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc cref="Title" />
    public string Message { get; set; } = string.Empty;

    /// <summary>The title as a message key plus arguments, or null for a row stored before keys existed.</summary>
    public string? TitleKey { get; set; }

    public IReadOnlyDictionary<string, object?>? TitleArgs { get; set; }

    /// <inheritdoc cref="TitleKey" />
    public string? MessageKey { get; set; }

    public IReadOnlyDictionary<string, object?>? MessageArgs { get; set; }

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

    /// <summary>
    /// Whether this alert type reaches the recipient's registered devices.
    ///
    /// Defaults to the same out-of-app threshold as email — see
    /// <c>NotificationOptions.PushMinSeverityOnByDefault</c> — because both are
    /// channels that interrupt somebody away from the app, and a user who finds push
    /// too loud opts a type out on this row rather than hunting for a global switch.
    /// </summary>
    public bool PushEnabled { get; set; }
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
    public List<string> Channels { get; set; } = new()
    {
        NotificationChannels.InApp,
        NotificationChannels.Email,
        NotificationChannels.Push
    };

    public string EmailMinSeverityOnByDefault { get; set; } = NotificationSeverity.Critical;

    /// <summary>
    /// The threshold push applied when it computed <c>PushEnabled</c> above.
    ///
    /// Separate from <see cref="EmailMinSeverityOnByDefault"/> only because the two
    /// settings are separate; they ship equal, and both are the reason a quieter alert
    /// type arrives switched off. A client that explains those defaults needs the number
    /// the server applied, not a copy of the default, and
    /// <c>PushSettingsDto.MinSeverityOnByDefault</c> reports the same value.
    /// </summary>
    public string MinSeverityOnByDefault { get; set; } = NotificationSeverity.Critical;
}

public class NotificationPreferenceUpdateDto
{
    public string AlertType { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }

    /// <summary>
    /// Whether this alert type reaches the recipient's devices, or null to leave the
    /// current choice alone.
    ///
    /// <para>
    /// Nullable on purpose. A client built before the push column existed sends rows
    /// without this field, and a plain <c>bool</c> would read that absence as "false" and
    /// mute push for every type that client saved — a settings screen silently changing a
    /// channel it does not know about. The other two channels have no such problem because
    /// they have always been in the payload.
    /// </para>
    /// </summary>
    public bool? PushEnabled { get; set; }
}

public class UpdateNotificationPreferencesRequest
{
    public List<NotificationPreferenceUpdateDto> Preferences { get; set; } = new();
}

/// <summary>
/// The delivery channels. In-app (the notification center), email and push are
/// implemented; SMS is not, and adding it is a new value here plus a new sender —
/// the per-(user, farm, alert type) preference model already has a slot for it.
/// </summary>
public static class NotificationChannels
{
    public const string InApp = "InApp";
    public const string Email = "Email";

    /// <summary>
    /// Web Push to the recipient's registered browsers. Unlike the other two this one
    /// needs a device to have registered itself, so "on" here means "and only if this
    /// person has a device" — see <c>PushSubscription</c>.
    /// </summary>
    public const string Push = "Push";
}
