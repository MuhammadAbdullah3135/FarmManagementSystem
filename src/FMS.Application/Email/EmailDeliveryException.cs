namespace FMS.Application.Email;

/// <summary>
/// A provider rejected a message, or could not be reached. Thrown by transports so a
/// caller can tell "this will never work" (a bad API key, an unverified sender) from
/// "this might work in a moment" (rate limiting, a provider fault) without knowing
/// anything about the provider.
///
/// The message is operator-facing: it carries the provider's own status and error
/// detail, and never the API key or the message body.
/// </summary>
public class EmailDeliveryException : Exception
{
    public EmailDeliveryException(
        string message,
        bool isTransient,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
        StatusCode = statusCode;
    }

    /// <summary>Rate limiting, a timeout, or a provider 5xx: retrying may succeed.</summary>
    public bool IsTransient { get; }

    /// <summary>The provider's HTTP status, when the failure had one.</summary>
    public int? StatusCode { get; }
}
