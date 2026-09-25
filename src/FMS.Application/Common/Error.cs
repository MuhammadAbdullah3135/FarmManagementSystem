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

    private Error(string code, string message, string? messageKey, IReadOnlyDictionary<string, object?>? messageArgs)
    {
        Code = code;
        Message = message;
        MessageKey = messageKey;
        MessageArgs = messageArgs;
    }

    public static Error NotFound(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("NotFound", message, messageKey, messageArgs);
    public static Error Validation(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Validation", message, messageKey, messageArgs);
    public static Error Unauthorized(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Unauthorized", message, messageKey, messageArgs);
    public static Error Conflict(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null)
        => new("Conflict", message, messageKey, messageArgs);
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
