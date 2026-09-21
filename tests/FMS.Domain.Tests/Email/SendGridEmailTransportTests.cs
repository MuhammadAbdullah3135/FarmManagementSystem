using System.Net;
using System.Text;
using System.Text.Json;
using FMS.Application.Email;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Email;

/// <summary>
/// The SendGrid transport is the only code in the application that talks to an email
/// provider, so these tests hold two things down at once:
///
/// <list type="number">
/// <item>which failures are worth another attempt (rate limiting, timeouts, provider
/// faults) and which are not (a rejected key, an unverified sender) — because retrying
/// the second kind only delays the operator learning about it; and</item>
/// <item>that the API key never reaches a log line, an exception, or a test failure
/// message, on any path including the exhausted-retry one. That is asserted by
/// capturing and scanning every entry rather than by reading the code.</item>
/// </list>
///
/// <para>
/// Retry waits are asserted, not slept through: the transport exposes a
/// <c>protected virtual</c> wait seam, and the test subclass records the delay it was
/// handed so a Retry-After can be checked exactly, including the cap that stops a
/// provider from stalling a user's request indefinitely.
/// </para>
/// </summary>
public class SendGridEmailTransportTests
{
    private const string ApiKey = "SG.super-secret-key-that-must-never-be-logged";

    private static readonly EmailMessage Message =
        new("farmer@example.com", "Reset your password", "plain body", "<p>html body</p>");

    // ── harness ────────────────────────────────────────────

    /// <summary>
    /// Returns queued responses in order, and records what it was asked to send.
    ///
    /// The recordings are snapshots taken while the request is still alive: the
    /// transport disposes each request after it is sent, so reading the message
    /// afterwards would be reading a disposed object. The last queued response is also
    /// reused, which is how a handler is told "fail every attempt".
    /// </summary>
    private sealed record SentRequest(string Method, string Path, string? AuthorizationScheme, string? AuthorizationParameter);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
        private Func<HttpRequestMessage, HttpResponseMessage>? _last;

        public List<SentRequest> Requests { get; } = new();

        public List<string> SentBodies { get; } = new();

        public SentRequest LastRequest => Requests[^1];

        public RecordingHandler Respond(HttpStatusCode status, string? body = null, TimeSpan? retryAfter = null) =>
            Respond(_ =>
            {
                var response = new HttpResponseMessage(status);
                if (body is not null)
                {
                    response.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                if (retryAfter is { } delay)
                {
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
                }

                return response;
            });

        public RecordingHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responses.Enqueue(factory);
            return this;
        }

        public RecordingHandler Throws(Exception exception)
        {
            _responses.Enqueue(_ => throw exception);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            SentBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            // A queued exception is thrown from here so it surfaces exactly where a
            // real socket failure would - inside HttpClient.SendAsync.
            if (_responses.Count > 0)
            {
                _last = _responses.Dequeue();
            }

            if (_last is null)
            {
                throw new InvalidOperationException("(test) the handler was called more times than responses were queued");
            }

            return _last(request);
        }
    }

    /// <summary>Records the retry delays instead of waiting them out.</summary>
    private sealed class RecordedTransport : SendGridEmailTransport
    {
        public List<TimeSpan> Waits { get; } = new();

        public RecordedTransport(
            HttpClient client,
            EmailOptions options,
            ILogger<SendGridEmailTransport> logger)
            : base(client, Options.Create(options), logger)
        {
        }

        protected override Task WaitBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Waits.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(RecordedTransport Transport, RecordingHandler Handler, CapturingLoggerProvider Logs)
        : IDisposable
    {
        public void Dispose() { }

        /// <summary>Every byte this transport wrote to the log, including exceptions.</summary>
        public string AllLogText => string.Join(
            "\n",
            Logs.Entries.Select(e =>
                $"{e.Level} {e.Category} {e.Message} {e.Exception?.ToString() ?? string.Empty}"));
    }

    private static Harness CreateHarness(Action<EmailOptions>? configure = null)
    {
        var options = new EmailOptions
        {
            Provider = EmailProviderKind.SendGrid,
            FromAddress = "no-reply@example.com",
            FromName = "Farm Management System",
            MaxAttempts = 2,
            RetryBaseDelaySeconds = 1,
            MaxRetryAfterSeconds = 10
        };

        options.SendGrid.ApiKey = ApiKey;
        configure?.Invoke(options);

        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.SendGrid.BaseUrl) };
        var (logger, logs) = TestLoggers.Create<SendGridEmailTransport>();

        return new Harness(new RecordedTransport(client, options, logger), handler, logs);
    }

    // ── the happy path ─────────────────────────────────────

    [Fact]
    public async Task SendAsync_Success_PostsOnceAndReturns()
    {
        using var harness = CreateHarness();
        harness.Handler.Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        Assert.Single(harness.Handler.Requests);
        Assert.Empty(harness.Transport.Waits);

        var request = harness.Handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v3/mail/send", request.Path);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ApiKey, request.AuthorizationParameter);
    }

    [Fact]
    public async Task SendAsync_Success_SendsTheAddressesSubjectAndBothBodies()
    {
        using var harness = CreateHarness();
        harness.Handler.Respond(HttpStatusCode.Accepted, "{\"message\":\"ok\"}");

        await harness.Transport.SendAsync(Message);

        using var payload = JsonDocument.Parse(harness.Handler.SentBodies[0]);
        var root = payload.RootElement;

        Assert.Equal("farmer@example.com",
            root.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("no-reply@example.com", root.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal(Message.Subject, root.GetProperty("subject").GetString());

        var content = root.GetProperty("content").EnumerateArray()
            .ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("value").GetString());

        // A message with no plain-text alternative is exactly what makes a password
        // reset arrive looking like spam, so both parts are part of the contract.
        Assert.Equal(Message.TextBody, content["text/plain"]);
        Assert.Equal(Message.HtmlBody, content["text/html"]);
    }

    // ── retryable failures ─────────────────────────────────

    [Fact]
    public async Task SendAsync_RateLimitedWithRetryAfter_HonoursTheProviderDelayAndSucceeds()
    {
        using var harness = CreateHarness();
        harness.Handler
            .Respond(HttpStatusCode.TooManyRequests, "{\"errors\":[{\"message\":\"too many requests\"}]}",
                retryAfter: TimeSpan.FromSeconds(3))
            .Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, harness.Transport.Waits);
    }

    [Fact]
    public async Task SendAsync_RateLimitedWithoutRetryAfter_FallsBackToLinearBackoff()
    {
        using var harness = CreateHarness();
        harness.Handler
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        // First retry waits base * attempt = 1s.
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, harness.Transport.Waits);
    }

    [Fact]
    public async Task SendAsync_LongRetryAfter_IsCapped()
    {
        // A provider asking for ten minutes must not hold a user's HTTP request open
        // for ten minutes; the cap is the whole point of MaxRetryAfterSeconds.
        using var harness = CreateHarness(o => o.MaxRetryAfterSeconds = 10);
        harness.Handler
            .Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMinutes(10))
            .Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        Assert.Equal(new[] { TimeSpan.FromSeconds(10) }, harness.Transport.Waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task SendAsync_TransientFailure_IsRetriedAndCanSucceed(HttpStatusCode status)
    {
        using var harness = CreateHarness();
        harness.Handler
            .Respond(status, "{\"errors\":[{\"message\":\"upstream\"}]}")
            .Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Single(harness.Transport.Waits);
    }

    [Fact]
    public async Task SendAsync_TransientFailureOnEveryAttempt_ThrowsTransientWithTheStatusAndAttemptCount()
    {
        using var harness = CreateHarness(o => o.MaxAttempts = 3);
        harness.Handler
            .Respond(HttpStatusCode.ServiceUnavailable, "{\"errors\":[{\"message\":\"maintenance\"}]}");

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.True(error.IsTransient);
        Assert.Equal(503, error.StatusCode);
        Assert.Equal(3, harness.Handler.Requests.Count);
        Assert.Contains("503", error.Message);
        Assert.Contains("3 attempt", error.Message);
        // The provider's own words reach the operator, so a support ticket starts
        // with the reason rather than "it failed".
        Assert.Contains("maintenance", error.Message);
        Assert.Equal(2, harness.Transport.Waits.Count);
    }

    [Fact]
    public async Task SendAsync_ConnectionFailure_IsRetriedThenThrownAsTransient()
    {
        using var harness = CreateHarness();
        harness.Handler.Throws(new HttpRequestException("(test) connection refused"));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.True(error.IsTransient);
        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Null(error.StatusCode);
        Assert.Contains("Could not reach SendGrid", error.Message);
    }

    [Fact]
    public async Task SendAsync_ClientTimeout_IsRetriedThenReportedAsATimeout()
    {
        using var harness = CreateHarness(o => o.RequestTimeoutSeconds = 1);
        harness.Handler.Throws(new TaskCanceledException("(test) the request timed out"));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.True(error.IsTransient);
        Assert.Contains("timed out", error.Message);
    }

    // ── deterministic failures: never retried ──────────────

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData((HttpStatusCode)413)]
    public async Task SendAsync_Rejection_IsNotRetried(HttpStatusCode status)
    {
        // A bad API key or an unverified sender (401/403) and a malformed request
        // (400) will never succeed on a second identical call; retrying only delays
        // the failure and burns the provider's rate limit.
        using var harness = CreateHarness(o => o.MaxAttempts = 5);
        harness.Handler.Respond(status, "{\"errors\":[{\"message\":\"rejected\"}]}");

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.False(error.IsTransient);
        Assert.Equal((int)status, error.StatusCode);
        Assert.Single(harness.Handler.Requests);
        Assert.Empty(harness.Transport.Waits);
        Assert.Contains("rejected", error.Message);
    }

    [Fact]
    public async Task SendAsync_CallerCancellation_IsNotRetriedAndPropagates()
    {
        // The caller giving up (a user closing the tab, a shutdown) is not a provider
        // fault, so it must not be retried or rewritten into a delivery error.
        using var harness = CreateHarness();
        using var cancellation = new CancellationTokenSource();
        harness.Handler.Respond(_ => throw new OperationCanceledException(cancellation.Token));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Transport.SendAsync(Message, cancellation.Token));

        Assert.Single(harness.Handler.Requests);
        Assert.Empty(harness.Transport.Waits);
    }

    [Fact]
    public async Task SendAsync_EmptyErrorBody_StillReportsTheStatus()
    {
        // SendGrid returns no body for some failures; the exception must stay
        // informative rather than carrying an empty detail string.
        using var harness = CreateHarness();
        harness.Handler.Respond(HttpStatusCode.Forbidden);

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.Contains("403", error.Message);
        Assert.Contains("no detail", error.Message);
    }

    [Fact]
    public async Task SendAsync_UnreadableErrorBody_DoesNotMaskTheFailure()
    {
        using var harness = CreateHarness();
        harness.Handler.Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
            response.Content = new StreamContent(new ThrowingStream());
            return response;
        });

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => harness.Transport.SendAsync(Message));

        Assert.Contains("400", error.Message);
        Assert.Contains("could not be read", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("(test) stream broken");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── the secret must never be observable ────────────────

    /// <summary>
    /// Every path that can log or throw, run with a capturing logger, then scanned for
    /// the key. A leak here would put a live credential for sending mail as the farm's
    /// address into a log aggregator, which is a worse outcome than a failed send.
    /// </summary>
    [Fact]
    public async Task Key_IsNeverLogged_OnSuccessOrAnyFailurePath()
    {
        var paths = new (string Name, Action<RecordingHandler, EmailOptions> Arrange)[]
        {
            ("success", (h, _) => h.Respond(HttpStatusCode.Accepted, "{\"message\":\"ok\"}")),
            ("rate limited then success", (h, _) => h
                .Respond(HttpStatusCode.TooManyRequests, "{\"errors\":[{\"message\":\"slow down\"}]}",
                    retryAfter: TimeSpan.FromSeconds(1))
                .Respond(HttpStatusCode.Accepted, "{}")),
            ("rejected key", (h, _) => h.Respond(HttpStatusCode.Unauthorized,
                "{\"errors\":[{\"message\":\"The provided authorization grant is invalid\"}]}")),
            ("exhausted retries", (h, o) =>
            {
                o.MaxAttempts = 2;
                h.Respond(HttpStatusCode.ServiceUnavailable, "{\"errors\":[{\"message\":\"maintenance\"}]}");
            }),
            ("unreachable", (h, _) => h.Throws(new HttpRequestException("(test) no route to host"))),
            ("request timed out", (h, _) => h.Throws(new TaskCanceledException("(test) timed out")))
        };

        foreach (var (name, arrange) in paths)
        {
            var options = new EmailOptions
            {
                Provider = EmailProviderKind.SendGrid,
                FromAddress = "no-reply@example.com",
                MaxAttempts = 2,
                RetryBaseDelaySeconds = 1
            };
            options.SendGrid.ApiKey = ApiKey;

            var handler = new RecordingHandler();
            arrange(handler, options);

            var client = new HttpClient(handler) { BaseAddress = new Uri(options.SendGrid.BaseUrl) };
            var (logger, logs) = TestLoggers.Create<SendGridEmailTransport>();
            var transport = new RecordedTransport(client, options, logger);

            var failure = await Record.ExceptionAsync(() => transport.SendAsync(Message));
            var everythingLogged = string.Join(
                "\n",
                logs.Entries.Select(e => $"{e.Level} {e.Category} {e.Message} {e.Exception}"));

            Assert.DoesNotContain(ApiKey, everythingLogged, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret-key", everythingLogged, StringComparison.Ordinal);

            if (failure is not null)
            {
                Assert.DoesNotContain(ApiKey, failure.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(ApiKey, failure.Message, StringComparison.Ordinal);
            }

            Assert.True(logs.Entries.Count > 0, $"the '{name}' path produced no log output at all");
        }
    }

    [Fact]
    public async Task SendAsync_Success_LogsTheRecipientAndProviderMessageIdButNotTheBody()
    {
        using var harness = CreateHarness();
        harness.Handler.Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.Add("X-Message-Id", "abc123");
            return response;
        });

        await harness.Transport.SendAsync(Message);

        Assert.Contains("farmer@example.com", harness.AllLogText);
        // The message id is how a delivery is traced with the provider, so it is
        // logged on purpose.
        Assert.Contains("abc123", harness.AllLogText);
    }

    [Fact]
    public async Task SendAsync_Retry_LogsWhyItIsRetrying()
    {
        using var harness = CreateHarness();
        harness.Handler
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Accepted, "{}");

        await harness.Transport.SendAsync(Message);

        // A retry nobody can see is indistinguishable from a hang.
        Assert.True(harness.Logs.ForLevel(LogLevel.Warning).Any(),
            "a retry must be logged at Warning with the status and attempt number");
        Assert.True(harness.Logs.HasMessageContaining("429"));
        Assert.True(harness.Logs.HasMessageContaining("2"));
    }
}
