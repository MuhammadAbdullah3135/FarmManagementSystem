namespace FMS.Application.Common;

/// <summary>
/// One problem found with one value, attributed to the field it belongs to.
///
/// The services' create rules return these rather than a bare message so that a bulk
/// caller can point at the offending column while the single-record endpoint keeps
/// answering with exactly the same text (<c>errors[0].Message</c>), which is what makes
/// "the import says the same thing as the endpoint" true by construction instead of by
/// careful maintenance.
/// </summary>
/// <para>
/// <see cref="MessageKey"/> and <see cref="MessageArgs"/> are the i18n addition, and they
/// are strictly additive: <see cref="Message"/> stays the English sentence it has always
/// been, so every existing caller and test keeps its byte-identical text, and the client
/// renders the key when it has one and the English fallback when it does not. A key never
/// changes even when its rendered text does, which is what keeps the two import paths and
/// the endpoint answering the same way in every language.
/// </para>
public readonly record struct FieldError(
    string Field,
    string Message,
    string? MessageKey = null,
    IReadOnlyDictionary<string, object?>? MessageArgs = null);

/// <summary>
/// A batch failure, reported by the item's position in the batch so the caller can
/// point at its own row.
/// </summary>
public class BulkCreateFailureDto
{
    public int Index { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// The outcome of validating or creating a batch.
///
/// Failures are values, not exceptions: a batch that half-fails is the normal case for
/// an import, and the caller needs every problem at once to show a user which rows to
/// fix.
/// </summary>
public class BulkCreateResultDto
{
    public int RequestedCount { get; set; }
    public int SuccessCount { get; set; }
    public List<BulkCreateFailureDto> Failures { get; set; } = new();
}
