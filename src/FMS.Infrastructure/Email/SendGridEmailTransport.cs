using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FMS.Application.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Email;

/// <summary>
/// Delivers mail through the SendGrid v3 HTTP API: one JSON POST, no SDK.
///
/// Chosen for this deployment because the volume is a handful of messages a day
/// (password resets, invitations, one digest per recipient per dispatch run), the API
/// is a single authenticated request from a container, and an API key plus a verified
/// sender is the whole setup.
///
/// Failure handling mirrors the notification dispatcher's asymmetry: this throws
/// <see cref="EmailDeliveryException"/> so the caller decides what a failed send
/// means. Transient failures (429, 408, 5xx, timeouts, connection errors) are retried
/// up to <see cref="EmailOptions.MaxAttempts"/> with backoff, honouring a
/// Retry-After when the provider sends one; a rejection (400, 401, 403 ...) is not
/// retried, because repeating it cannot help.
/// </summary>
public class SendGridEmailTransport : IEmailTransport
{
    /// <summary>The only endpoint used; combined with the client's base address.</summary>
    public const string MailSendPath = "/v3/mail/send";

    private const int MaxErrorDetailLength = 500;

    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly ILogger<SendGridEmailTransport> _logger;

    public SendGridEmailTransport(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        ILogger<SendGridEmailTransport> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var attempts = Math.Max(1, _options.MaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= attempts;

            using var request = BuildRequest(message);
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (!isLastAttempt)
                {
                    await WaitBeforeRetryAsync(Backoff(attempt), cancellationToken);
                    continue;
                }

                _logger.LogError(ex,
                    "Could not reach SendGrid for {Recipient} after {Attempt} attempt(s); giving up.",
                    message.To,
                    attempt);

                throw new EmailDeliveryException(
                    $"Could not reach SendGrid for {message.To} after {attempt} attempt(s): {ex.Message}",
                    isTransient: true,
                    innerException: ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The client's own timeout, not the caller cancelling: worth a retry.
                if (!isLastAttempt)
                {
                    await WaitBeforeRetryAsync(Backoff(attempt), cancellationToken);
                    continue;
                }

                _logger.LogError(ex,
                    "Sending to {Recipient} timed out after {Attempt} attempt(s) at {TimeoutSeconds}s per attempt; giving up.",
                    message.To,
                    attempt,
                    _httpClient.Timeout.TotalSeconds);

                throw new EmailDeliveryException(
                    $"Sending to {message.To} timed out after {attempt} attempt(s) at "
                    + $"{_httpClient.Timeout.TotalSeconds:0.#}s per attempt.",
                    isTransient: true,
                    innerException: ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Sent email to {Recipient} through SendGrid on attempt {Attempt}. Provider message id: {MessageId}",
                        message.To,
                        attempt,
                        MessageId(response) ?? "(none)");

                    return;
                }

                var detail = await DescribeFailureAsync(response, cancellationToken);
                var isTransient = IsTransient(response.StatusCode);

                if (!isTransient || isLastAttempt)
                {
                    // Logged here as well as thrown: a caller may record the failure as a
                    // flag (the notification dispatcher does), and the provider's own words
                    // are what makes a delivery problem diagnosable. Status, recipient and
                    // provider detail only - never the key, never the message body.
                    _logger.LogError(
                        "SendGrid rejected the message for {Recipient} with {StatusCode} {Reason} "
                        + "after {Attempt} attempt(s); no further attempts will be made. {Detail}",
                        message.To,
                        (int)response.StatusCode,
                        response.StatusCode,
                        attempt,
                        detail);

                    throw new EmailDeliveryException(
                        $"SendGrid rejected the message for {message.To} with "
                        + $"{(int)response.StatusCode} {response.StatusCode} after {attempt} attempt(s). {detail}",
                        isTransient,
                        (int)response.StatusCode);
                }

                _logger.LogWarning(
                    "SendGrid returned {StatusCode} for {Recipient} on attempt {Attempt} of {Attempts}; retrying.",
                    (int)response.StatusCode,
                    message.To,
                    attempt,
                    attempts);

                await WaitBeforeRetryAsync(RetryDelay(response) ?? Backoff(attempt), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Waits between attempts. Virtual so tests can assert the delay that was chosen -
    /// including a provider's Retry-After - without actually waiting for it.
    /// </summary>
    protected virtual Task WaitBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    /// <summary>Backoff before the next attempt: the configured base multiplied by the attempt number.</summary>
    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Max(0, _options.RetryBaseDelaySeconds) * attempt);

    /// <summary>The provider's Retry-After, capped, or null when it did not send one.</summary>
    private TimeSpan? RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        var delay = retryAfter.Delta
            ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        var cap = TimeSpan.FromSeconds(Math.Max(0, _options.MaxRetryAfterSeconds));
        return delay > cap ? cap : delay;
    }

    /// <summary>
    /// Rate limiting, a request timeout and provider-side faults are worth another
    /// attempt; anything else is a rejected message, and repeating it only wastes time.
    /// </summary>
    private static bool IsTransient(HttpStatusCode statusCode) =>
        (int)statusCode == 408 || (int)statusCode == 429 || (int)statusCode >= 500;

    private static string? MessageId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Message-Id", out var values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// Reads the provider's error detail for the operator. Never includes the request,
    /// the API key or the message body.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return "The provider returned no detail.";
            }

            var trimmed = body.Length > MaxErrorDetailLength ? body[..MaxErrorDetailLength] + "..." : body;
            return $"Provider response: {trimmed}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"The provider's response could not be read: {ex.Message}";
        }
    }

    private HttpRequestMessage BuildRequest(EmailMessage message)
    {
        var payload = new SendGridMailRequest(
            Personalizations: new[]
            {
                new SendGridPersonalization(new[] { new SendGridRecipient(message.To) })
            },
            From: new SendGridAddress(_options.FromAddress, _options.FromName),
            Subject: message.Subject,
            Content: new[]
            {
                new SendGridContent("text/plain", message.TextBody),
                new SendGridContent("text/html", message.HtmlBody)
            });

        var request = new HttpRequestMessage(HttpMethod.Post, MailSendPath)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SendGrid.ApiKey);

        return request;
    }

    private sealed record SendGridMailRequest(
        [property: JsonPropertyName("personalizations")] SendGridPersonalization[] Personalizations,
        [property: JsonPropertyName("from")] SendGridAddress From,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("content")] SendGridContent[] Content);

    private sealed record SendGridPersonalization(
        [property: JsonPropertyName("to")] SendGridRecipient[] To);

    private sealed record SendGridRecipient(
        [property: JsonPropertyName("email")] string Email);

    private sealed record SendGridAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record SendGridContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value);
}
