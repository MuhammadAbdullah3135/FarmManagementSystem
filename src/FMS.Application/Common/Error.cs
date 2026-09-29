namespace FMS.Application.Common;

public sealed class Error
{
    /// <summary>
    /// The code for <see cref="Superseded"/>.
    ///
    /// Named here because two layers have to agree on it: the workflow services that report
    /// "your intent is already satisfied" and the sync layer that has to tell that apart from a
    /// refusal. A string spelled in both places is a spelling that can drift.
    /// </summary>
    public const string SupersededCode = "Superseded";

    /// <summary>The code for <see cref="Unavailable"/>.</summary>
    public const string UnavailableCode = "Unavailable";

    public string Code { get; }
    public string Message { get; }

    /// <summary>
    /// The i18n key for <see cref="Message"/>, or null when the message has no key yet.
    ///
    /// Additive by design: <see cref="Message"/> remains the exact English sentence the
    /// application has always returned, so nothing that asserts on it changes, while a
    /// caller that understands keys can localise without the server knowing a locale.
    /// </summary>
    public string? MessageKey { get; }

    /// <summary>Interpolation values for <see cref="MessageKey"/>, or null when it takes none.</summary>
    public IReadOnlyDictionary<string, object?>? MessageArgs { get; }

    /// <summary>
    /// The rows standing in the way of a refused change, or null when the refusal is not about
    /// other rows. A conflict that only counts what blocks it ("still used by 2 gestation
    /// records") sends the reader hunting; one that names the rows can link straight to them.
    ///
    /// <para>
    /// The count stays in <see cref="Message"/> and the key stays in <see cref="MessageKey"/>,
    /// so every existing caller and test reads what it always read. Only a caller that wants the
    /// rows reads this, and a caller that ignores it loses nothing — the same additivity rule the
    /// key itself follows.
    /// </para>
    /// </summary>
    public IReadOnlyList<Blocker>? Blockers { get; }

    private Error(string code, string message, string? messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs, IReadOnlyList<Blocker>? blockers = null)
    {
        Code = code;
        Message = message;
        MessageKey = messageKey;
        MessageArgs = messageArgs;
        Blockers = blockers;
    }

    public static Error NotFound(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("NotFound", message, messageKey, messageArgs);
    public static Error Validation(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Validation", message, messageKey, messageArgs);
    public static Error Unauthorized(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Unauthorized", message, messageKey, messageArgs);
    public static Error Conflict(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Conflict", message, messageKey, messageArgs);

    /// <summary>A conflict that can name the rows in the way. The message and key are unchanged.</summary>
    public static Error Conflict(string message, string messageKey, IReadOnlyList<Blocker> blockers)
        => new("Conflict", message, messageKey, null, blockers);

    public static Error Unexpected(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Unexpected", message, messageKey, messageArgs);

    /// <summary>
    /// This deployment cannot do the work at all — not a validation failure of the request and
    /// not a transient fault. Retrying the same request will not change the outcome, and the
    /// caller is owed that fact rather than a 500 that reads like a bug.
    ///
    /// The one caller today is the full-farm export when the background job subsystem is
    /// switched off: the request is perfectly well formed, and nothing in this process would
    /// ever build the archive.
    /// </summary>
    public static Error Unavailable(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new(UnavailableCode, message, messageKey, messageArgs);

    /// <summary>
    /// The change was not applied because existing state already means what the caller asked
    /// for — nothing was written, and nothing is wrong.
    ///
    /// This exists because a queued item cannot be answered with a status code. "Already done"
    /// has to let a device clear the item from its queue, while "refused" has to keep it and
    /// show a human why: the two cases often look identical over HTTP (both are 409), and a
    /// queue that guesses between them by reading message text is a queue that breaks the next
    /// time a message is reworded.
    /// </summary>
    public static Error Superseded(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new(SupersededCode, message, messageKey, messageArgs);


    public override string ToString() => $"{Code}: {Message}";
}
