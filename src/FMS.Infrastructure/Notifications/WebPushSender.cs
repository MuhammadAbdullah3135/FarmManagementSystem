using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FMS.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Notifications;

/// <summary>
/// Delivers a push through the browser's push service, the way RFC 8030 describes and
/// with the message encrypted as RFC 8291 requires.
///
/// <para>
/// The shape of this class is decided by who calls it. It runs inside the scheduled
/// dispatch, once per recipient with a registered device, so:
/// </para>
///
/// <list type="bullet">
/// <item>it never throws — a push service having a bad afternoon must not fail a farm's
/// dispatch, and the notification rows are already persisted by the time this runs;</item>
/// <item>it reports <see cref="PushSendResult.SubscriptionGone"/> separately, because a
/// 404 or 410 means the browser is gone for good and the caller should stop trying,
/// while any other failure is worth retrying on the next run.</item>
/// </list>
///
/// <para>
/// A deployment with no keys is not an error state: <see cref="IsConfigured"/> is false,
/// every send reports why, and the dispatcher skips the channel. That is what lets the
/// whole feature exist in a checkout with no credentials — the same shape the email
/// transport uses when it is set to log instead of send.
/// </para>
/// </summary>
public class WebPushSender : IPushSender
{
    private const string TimeoutReason = "the push service did not answer in time";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PushOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly VapidKeys? _keys;

    public WebPushSender(
        HttpClient httpClient,
        IOptions<PushOptions> options,
        ILogger<WebPushSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Parsed once, at construction: a key pair that does not load would otherwise be
        // discovered one alert at a time, on somebody's phone, as silence.
        if (VapidKeys.TryParse(_options.PublicKey, _options.PrivateKey, out var keys, out var problem))
        {
            _keys = keys;
        }
        else if (_options.Enabled)
        {
            _logger.LogError(
                "Push is enabled but its VAPID keys are unusable: {Problem} No push messages will be sent.",
                problem);
        }
    }

    /// <inheritdoc />
    public bool IsConfigured =>
        _options.Enabled && _keys is not null && !string.IsNullOrWhiteSpace(_options.Subject);

    /// <inheritdoc />
    public async Task<PushSendResult> SendAsync(
        PushSubscriptionTarget target,
        PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_keys is null || !IsConfigured)
        {
            return PushSendResult.Failed("push is not configured on this deployment");
        }

        var authorization = _keys.BuildAuthorizationHeader(target.Endpoint, _options.Subject);
        if (authorization is null)
        {
            return PushSendResult.Failed("the stored subscription endpoint is not an https URL");
        }

        byte[] body;
        try
        {
            body = WebPushEncryption.Encrypt(
                target.P256dh, target.Auth, BuildPayload(message));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // A stored key that will not parse is not the push service's fault and will
            // not fix itself, so this is a permanent failure worth naming in the log —
            // but it must not reach the top of a scheduled job either.
            return PushSendResult.Failed($"the stored subscription keys are unusable: {ex.Message}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint)
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add(WebPushEncryption.ContentEncoding);

        // How long the push service may keep trying. Sent as seconds, per RFC 8030 §5.2.
        request.Headers.TryAddWithoutValidation("TTL", _options.TtlSeconds.ToString());

        // "high" for anything Critical: the urgency hint is how a phone decides to wake
        // for this rather than wait for its next maintenance window.
        request.Headers.TryAddWithoutValidation(
            "Urgency",
            string.Equals(message.Severity, NotificationSeverity.Critical, StringComparison.Ordinal)
                ? "high"
                : "normal");

        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return PushSendResult.Sent;
            }

            // 404 and 410 are RFC 8030 §7.3's "this subscription no longer exists". The
            // browser was uninstalled, the profile deleted, or permission revoked: no
            // amount of retrying brings it back, so the caller disables the row.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return PushSendResult.Gone(
                    $"the push service answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return PushSendResult.Failed(
                $"the push service answered {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PushSendResult.Failed(TimeoutReason);
        }
        catch (HttpRequestException ex)
        {
            // DNS, TLS, a refused connection: transient as far as this end knows.
            return PushSendResult.Failed($"the request to the push service failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The message as the service worker will read it.
    ///
    /// <para>
    /// Field names are the ones the worker looks for, so the payload is a contract
    /// between this class and <c>tools/offline/sw-template.ts</c> — both sides are
    /// asserted by tests, because nothing else would catch them drifting apart.
    /// </para>
    ///
    /// <para>
    /// Oversized messages are trimmed rather than dropped. The record size is a hard
    /// ceiling (RFC 8291 §4 forbids splitting a message across records), and a long
    /// animal name in a long alert sentence could reach it; an alert that arrives with a
    /// shortened sentence beats one a push service rejects as malformed.
    /// </para>
    /// </summary>
    private static byte[] BuildPayload(PushMessage message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new PushPayload(message.Title, message.Body, message.Url, message.Tag, message.Severity, message.AlertCount),
            PayloadOptions);

        while (payload.Length > WebPushEncryption.MaxPayloadLength && message.Body.Length > 0)
        {
            // Drop a tenth of the body each pass: bounded, and it terminates.
            var keep = Math.Max(0, message.Body.Length - Math.Max(1, message.Body.Length / 10));
            message = message with { Body = keep == 0 ? string.Empty : message.Body[..keep] };

            payload = JsonSerializer.SerializeToUtf8Bytes(
                new PushPayload(message.Title, message.Body, message.Url, message.Tag, message.Severity, message.AlertCount),
                PayloadOptions);
        }

        return payload;
    }

    /// <summary>The wire format the service worker reads. See <see cref="BuildPayload"/>.</summary>
    private sealed record PushPayload(
        string Title,
        string Body,
        string? Url,
        string Tag,
        string Severity,
        int Count);
}
