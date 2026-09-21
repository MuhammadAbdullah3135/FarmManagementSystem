namespace FMS.Application.Email;

/// <summary>Which transport actually delivers mail.</summary>
public enum EmailProviderKind
{
    /// <summary>
    /// Render the message and log it instead of sending it. This is the behaviour the
    /// application shipped with (the "PLACEHOLDER EMAIL SERVICE" log lines), kept as an
    /// explicit, selectable mode so that a fresh clone, the test host and every
    /// automated run need no credentials, and so that Development can still complete a
    /// password reset by copying the link out of the log.
    ///
    /// It is not delivery. EmailConfigurationGuard warns when a non-Development
    /// environment runs with it.
    /// </summary>
    Log = 0,

    /// <summary>SendGrid's v3 HTTP API (a single JSON POST).</summary>
    SendGrid = 1
}

/// <summary>
/// Email delivery configuration, bound from the <c>Email</c> section.
///
/// Every value has a safe default, and <see cref="Provider"/> defaults to
/// <see cref="EmailProviderKind.Log"/>, so deploying this changes nothing until
/// credentials are supplied.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProviderKind Provider { get; set; } = EmailProviderKind.Log;

    /// <summary>
    /// Envelope sender. Required by every real provider, and it must be an address
    /// the provider has verified, or the message is rejected.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Farm Management System";

    /// <summary>
    /// Per-attempt HTTP timeout. Deliberately short: password-reset and invitation
    /// sends happen inside the HTTP request, so a provider that hangs must not hang
    /// the caller with it.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>Total attempts, including the first. Only transient failures are retried.</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Backoff before retry N is this many seconds multiplied by N.</summary>
    public int RetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Ceiling on a provider's Retry-After, so a rate-limited response cannot stall a
    /// user's request for as long as the provider feels like asking.
    /// </summary>
    public int MaxRetryAfterSeconds { get; set; } = 10;

    public SendGridOptions SendGrid { get; set; } = new();

    /// <summary>True when a provider is selected, i.e. when mail is actually delivered.</summary>
    public bool SendsRealMail => Provider != EmailProviderKind.Log;
}

public class SendGridOptions
{
    /// <summary>A bearer credential for sending as the configured sender. Never logged, never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.sendgrid.com";
}
