using FMS.Application.Auth;
using FMS.Application.Email;
using FMS.Application.Notifications;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Email;

/// <summary>
/// The one thing no unit test can prove: that a real provider accepts these messages and
/// a real mailbox receives them. Rendering, retry and error handling are covered
/// elsewhere; what is unproven by them is deliverability — a sender the provider has not
/// verified, a body it rejects, a link that a mail client rewrites.
///
/// <para>
/// These SKIP unless the environment supplies a recipient and credentials, because
/// sending mail from an automated run is not something a test suite should do on its own.
/// To run them, set:
/// <code>
/// FMS_TEST_EMAIL=you@example.com
/// FMS_TEST_EMAIL_PROVIDER=SendGrid
/// FMS_TEST_SENDGRID_API_KEY=SG.…
/// FMS_TEST_EMAIL_FROM=noreply@your-verified-sender.com
/// FMS_TEST_FRONTEND_URL=https://your-frontend.example.com   # optional
/// </code>
/// and then <c>dotnet test --filter FullyQualifiedName~RealEmailDeliveryTests</c>. Each
/// test sends one message; check the inbox and report back which arrived.
/// </para>
/// </summary>
public class RealEmailDeliveryTests
{
    private const string RecipientVariable = "FMS_TEST_EMAIL";
    private const string ProviderVariable = "FMS_TEST_EMAIL_PROVIDER";
    private const string ApiKeyVariable = "FMS_TEST_SENDGRID_API_KEY";
    private const string FromVariable = "FMS_TEST_EMAIL_FROM";
    private const string FrontendUrlVariable = "FMS_TEST_FRONTEND_URL";

    private static string? Recipient => Environment.GetEnvironmentVariable(RecipientVariable);

    private static string? ApiKey => Environment.GetEnvironmentVariable(ApiKeyVariable);

    private static string? FromAddress => Environment.GetEnvironmentVariable(FromVariable);

    /// <summary>
    /// Why these tests cannot run, phrased so the skip message is itself the runbook.
    /// </summary>
    private static string? UnavailableReason()
    {
        if (Environment.GetEnvironmentVariable(ProviderVariable) is not { Length: > 0 } provider)
        {
            return $"Set {ProviderVariable}=SendGrid (plus {RecipientVariable}, {FromVariable} and "
                + $"{ApiKeyVariable}) to send real mail. Skipping: no live provider is configured.";
        }

        if (!string.Equals(provider, "SendGrid", StringComparison.OrdinalIgnoreCase))
        {
            return $"Unknown {ProviderVariable}='{provider}'. This test knows how to drive SendGrid only.";
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Recipient)) missing.Add(RecipientVariable);
        if (string.IsNullOrWhiteSpace(FromAddress)) missing.Add(FromVariable);
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add(ApiKeyVariable);

        return missing.Count == 0
            ? null
            : $"Set {string.Join(", ", missing)} to send real mail. Skipping: a live send was requested but not fully configured.";
    }

    private static void SkipIfNotConfigured()
    {
        var reason = UnavailableReason();
        Skip.If(reason is not null, reason);
    }

    private static EmailService CreateService(out RecordingTransport transport)
    {
        var frontendUrl = Environment.GetEnvironmentVariable(FrontendUrlVariable) ?? "https://example.com";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = frontendUrl,
                ["Invitations:ExpiryDays"] = "7"
            })
            .Build();

        var options = new EmailOptions
        {
            Provider = EmailProviderKind.SendGrid,
            FromAddress = FromAddress!,
            FromName = "Farm Management System (delivery test)",
            SendGrid = { ApiKey = ApiKey! }
        };

        var httpClient = new HttpClient { BaseAddress = new Uri(options.SendGrid.BaseUrl) };
        var sendGrid = new SendGridEmailTransport(
            httpClient, Options.Create(options), NullLogger<SendGridEmailTransport>.Instance);

        // The real provider, wrapped so the test can also show what it rendered.
        transport = new RecordingTransport(sendGrid);
        return new EmailService(transport, configuration);
    }

    /// <summary>Passes through to the real transport and keeps a copy of each message.</summary>
    private sealed class RecordingTransport : IEmailTransport
    {
        private readonly IEmailTransport _inner;

        public RecordingTransport(IEmailTransport inner) => _inner = inner;

        public List<EmailMessage> Sent { get; } = new();

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            await _inner.SendAsync(message, cancellationToken);
            Sent.Add(message);
        }
    }

    private static void Report(string flow, EmailMessage message)
    {
        // Printed so the run's output can be pasted back as the evidence for this flow.
        Console.WriteLine($"[real-email] {flow} -> {message.To} | subject: {message.Subject}");
        Console.WriteLine($"[real-email] {flow} link(s): "
            + string.Join(" ", ExtractLinks(message.TextBody)));
    }

    private static IEnumerable<string> ExtractLinks(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));

    [SkippableFact]
    public async Task PasswordReset_IsDeliveredToARealInbox()
    {
        SkipIfNotConfigured();

        var service = CreateService(out var transport);
        var link = $"{(Environment.GetEnvironmentVariable(FrontendUrlVariable) ?? "https://example.com").TrimEnd('/')}"
            + $"/confirm-reset-password?token={Guid.NewGuid():N}";

        await service.SendPasswordResetEmailAsync(Recipient!, link);

        var message = Assert.Single(transport.Sent);
        // A throwing transport would have failed the call; reaching here means the provider
        // accepted the message (202) and returned a message id.
        Report("password reset", message);
        Assert.Contains(link, message.TextBody);
    }

    [SkippableFact]
    public async Task FarmInvitation_IsDeliveredToARealInbox()
    {
        SkipIfNotConfigured();

        var service = CreateService(out var transport);
        var link = $"{(Environment.GetEnvironmentVariable(FrontendUrlVariable) ?? "https://example.com").TrimEnd('/')}"
            + $"/accept-invitation?token={Guid.NewGuid():N}";

        await service.SendFarmInvitationEmailAsync(Recipient!, "Delivery Test Farm", link);

        var message = Assert.Single(transport.Sent);
        Report("farm invitation", message);
        Assert.Contains(link, message.TextBody);
    }

    [SkippableFact]
    public async Task AlertDigest_IsDeliveredToARealInbox()
    {
        SkipIfNotConfigured();

        var service = CreateService(out var transport);

        await service.SendAlertNotificationsEmailAsync(
            Recipient!,
            "Delivery Test Farm",
            new[]
            {
                new NotificationEmailItem(
                    NotificationSeverity.Critical,
                    "3 vaccinations overdue",
                    "Cattle in pen B are past their vaccination due date.",
                    "/dashboard/health/vaccinations"),
                new NotificationEmailItem(
                    NotificationSeverity.Warning,
                    "2 feed tasks overdue",
                    "Morning feeding was not recorded for 2 pens.",
                    "/dashboard/tasks")
            });

        var message = Assert.Single(transport.Sent);
        Report("alert digest", message);

        // The digest's links are stored relative, so this is also the proof that they were
        // absolutised against the configured frontend URL before being sent.
        Assert.Contains("http", message.TextBody);
        Assert.Contains("/dashboard/tasks", message.TextBody);
    }
}
