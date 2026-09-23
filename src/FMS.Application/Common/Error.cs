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

    public string Code { get; }
    public string Message { get; }

    private Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public static Error NotFound(string message) => new("NotFound", message);
    public static Error Validation(string message) => new("Validation", message);
    public static Error Unauthorized(string message) => new("Unauthorized", message);
    public static Error Conflict(string message) => new("Conflict", message);
    public static Error Unexpected(string message) => new("Unexpected", message);

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
    public static Error Superseded(string message) => new(SupersededCode, message);


    public override string ToString() => $"{Code}: {Message}";
}
