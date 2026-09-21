using FMS.Application.Auth;
using FMS.Application.Email;
using FMS.Application.Notifications;
using FMS.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;

namespace FMS.Domain.Tests.Email;

/// <summary>
/// The three messages the application sends, rendered and asserted.
///
/// Two things here only became relevant once a real provider was introduced, and both
/// would have been invisible while the placeholder only wrote to the log:
///
/// <list type="bullet">
/// <item>the digest's links are stored as frontend-relative paths
/// ("/dashboard/notifications"), which are meaningless in an inbox, so they are
/// resolved against <c>Frontend:BaseUrl</c> — the same setting the reset and
/// invitation links are built from; and</item>
/// <item>farm names, alert titles and alert messages are user input rendered into an
/// HTML document, so they are encoded. A farm named <c>&lt;script&gt;</c> belongs in
/// the body as text, not as markup.</item>
/// </list>
///
/// <para>
/// These run against a recording transport rather than SendGrid: rendering is separate
/// from delivery by design, and that separation is exactly what makes it testable
/// without a network.
/// </para>
/// </summary>
public class EmailServiceRenderingTests
{
    // ── harness ────────────────────────────────────────────

    private sealed class RecordingTransport : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = new();

        public EmailMessage Last => Sent[^1];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static (EmailService Service, RecordingTransport Transport) Create(
        string? frontendBaseUrl = "https://farm.example.com",
        string? invitationExpiryDays = null)
    {
        var settings = new Dictionary<string, string?>();
        if (frontendBaseUrl is not null)
        {
            settings["Frontend:BaseUrl"] = frontendBaseUrl;
        }

        if (invitationExpiryDays is not null)
        {
            settings["Invitations:ExpiryDays"] = invitationExpiryDays;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var transport = new RecordingTransport();

        return (new EmailService(transport, configuration), transport);
    }

    private static NotificationEmailItem Alert(
        string title,
        string message,
        string? link = null,
        string severity = NotificationSeverity.Critical) =>
        new(severity, title, message, link);

    // ── password reset ─────────────────────────────────────

    [Fact]
    public async Task PasswordReset_RendersBothBodiesWithTheLinkAndTheRecipient()
    {
        var (service, transport) = Create();
        const string link = "https://farm.example.com/confirm-reset-password?token=abc123";

        await service.SendPasswordResetEmailAsync("farmer@example.com", link);

        var message = Assert.Single(transport.Sent);
        Assert.Equal("farmer@example.com", message.To);
        Assert.Contains("password", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(link, message.TextBody);
        Assert.Contains(link, message.HtmlBody);
        // Some clients strip anchors, so the URL must also be readable as text.
        Assert.Contains(link, message.TextBody);
        Assert.Contains("expires in one hour", message.TextBody);
        Assert.Contains("expires in one hour", message.HtmlBody);
    }

    [Fact]
    public async Task PasswordReset_EscapesTheLinkInHtml()
    {
        var (service, transport) = Create();

        await service.SendPasswordResetEmailAsync(
            "farmer@example.com", "https://farm.example.com/reset?token=a&b=2");

        // An unescaped ampersand in an href is a malformed attribute, and a link that
        // breaks in the mail client is a reset nobody can complete.
        Assert.Contains("token=a&amp;b=2", transport.Last.HtmlBody);
        Assert.DoesNotContain("token=a&b=2", transport.Last.HtmlBody);
        // The plain-text part is not HTML, so it keeps the literal URL.
        Assert.Contains("token=a&b=2", transport.Last.TextBody);
    }

    [Fact]
    public async Task PasswordReset_EscapesTheAddressInHtml()
    {
        var (service, transport) = Create();

        await service.SendPasswordResetEmailAsync(
            "a\"onmouseover=\"x@example.com", "https://farm.example.com/reset?token=abc");

        Assert.DoesNotContain("onmouseover=\"x\"", transport.Last.HtmlBody);
        Assert.Contains("&quot;", transport.Last.HtmlBody);
    }

    // ── farm invitation ────────────────────────────────────

    [Fact]
    public async Task Invitation_RendersTheFarmNameTheLinkAndTheConfiguredExpiry()
    {
        var (service, transport) = Create(invitationExpiryDays: "3");

        await service.SendFarmInvitationEmailAsync(
            "invitee@example.com", "Green Acres", "https://farm.example.com/accept-invitation?token=xyz");

        var message = Assert.Single(transport.Sent);
        Assert.Equal("invitee@example.com", message.To);
        Assert.Contains("Green Acres", message.Subject);
        Assert.Contains("Green Acres", message.HtmlBody);
        Assert.Contains("https://farm.example.com/accept-invitation?token=xyz", message.HtmlBody);
        // The email quotes the same lifetime the service gives the token (7 by
        // default), so the promise and the expiry cannot drift apart.
        Assert.Contains("expires in 3 days", message.TextBody);
    }

    [Fact]
    public async Task Invitation_DefaultsToSevenDaysWhenNotConfigured()
    {
        var (service, transport) = Create();

        await service.SendFarmInvitationEmailAsync(
            "invitee@example.com", "Green Acres", "https://farm.example.com/accept?token=xyz");

        Assert.Contains("expires in 7 days", transport.Last.TextBody);
    }

    [Fact]
    public async Task Invitation_EscapesAHostileFarmName()
    {
        var (service, transport) = Create();

        await service.SendFarmInvitationEmailAsync(
            "invitee@example.com", "<script>alert('x')</script> & Sons", "https://farm.example.com/accept?token=xyz");

        Assert.Contains("&lt;script&gt;", transport.Last.HtmlBody);
        Assert.DoesNotContain("<script>", transport.Last.HtmlBody);
        Assert.Contains("&amp;", transport.Last.HtmlBody);
        // Plain text carries it literally, which is correct — it is not markup.
        Assert.Contains("<script>alert('x')</script> & Sons", transport.Last.TextBody);
    }

    [Fact]
    public async Task Invitation_SubjectStaysOnOneLineWhenTheFarmNameHasLineBreaks()
    {
        var (service, transport) = Create();

        await service.SendFarmInvitationEmailAsync(
            "invitee@example.com", "Green\r\nAcres", "https://farm.example.com/accept?token=xyz");

        // A newline in a header is header injection, and a client may reject the message.
        Assert.DoesNotContain("\n", transport.Last.Subject);
        Assert.DoesNotContain("\r", transport.Last.Subject);
        Assert.Contains("Green", transport.Last.Subject);
        Assert.Contains("Acres", transport.Last.Subject);
    }

    [Fact]
    public async Task Invitation_FallsBackWhenTheFarmHasNoName()
    {
        var (service, transport) = Create();

        await service.SendFarmInvitationEmailAsync(
            "invitee@example.com", "   ", "https://farm.example.com/accept?token=xyz");

        Assert.Contains("a farm", transport.Last.Subject);
        Assert.Contains("a farm", transport.Last.TextBody);
    }

    // ── alert digest ───────────────────────────────────────

    [Fact]
    public async Task Digest_AbsolutisesRelativeAlertLinks()
    {
        var (service, transport) = Create(frontendBaseUrl: "https://farm.example.com");

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com",
            "Green Acres",
            new[]
            {
                Alert("3 vaccinations overdue", "Cattle need vaccinating.", "/dashboard/health/vaccinations"),
                Alert("2 tasks overdue", "Feed tasks are late.", "/dashboard/tasks", NotificationSeverity.Warning)
            });

        var message = Assert.Single(transport.Sent);
        Assert.Equal("manager@example.com", message.To);
        Assert.Contains("https://farm.example.com/dashboard/health/vaccinations", message.HtmlBody);
        Assert.Contains("https://farm.example.com/dashboard/tasks", message.HtmlBody);
        Assert.Contains("https://farm.example.com/dashboard/health/vaccinations", message.TextBody);

        // The stored path is frontend-relative; a bare path in an inbox is a dead link,
        // so it must not appear unrooted.
        Assert.DoesNotContain("href=\"/dashboard/tasks\"", message.HtmlBody);
    }

    [Fact]
    public async Task Digest_TrimsATrailingSlashOnTheFrontendBaseUrl()
    {
        var (service, transport) = Create(frontendBaseUrl: "https://farm.example.com/");

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com", "Green Acres", new[] { Alert("Late", "Task overdue.", "/dashboard/tasks") });

        // "https://host//dashboard" is a different path, and not always a redirect.
        Assert.Contains("https://farm.example.com/dashboard/tasks", transport.Last.HtmlBody);
        Assert.DoesNotContain("example.com//dashboard", transport.Last.HtmlBody);
    }

    [Fact]
    public async Task Digest_LeavesAbsoluteLinksAlone()
    {
        var (service, transport) = Create();

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com",
            "Green Acres",
            new[] { Alert("Exported report", "The monthly report is ready.", "https://reports.example.com/r/42") });

        Assert.Contains("https://reports.example.com/r/42", transport.Last.HtmlBody);
        Assert.DoesNotContain("farm.example.comhttps://reports", transport.Last.HtmlBody);
    }

    [Fact]
    public async Task Digest_OmitsTheLinkLineWhenThereIsNone()
    {
        var (service, transport) = Create();

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com", "Green Acres", new[] { Alert("Late", "Task overdue.") });

        Assert.DoesNotContain("href=\"https://farm.example.com/\"", transport.Last.HtmlBody);
        Assert.Contains("Task overdue.", transport.Last.HtmlBody);
    }

    [Fact]
    public async Task Digest_LinksToTheNotificationCentreAndPreferences()
    {
        var (service, transport) = Create(frontendBaseUrl: "https://farm.example.com");

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com", "Green Acres", new[] { Alert("Late", "Task overdue.") });

        // What turns an alert email into something actionable rather than noise: the
        // path to the record, and the path to turning this type of mail off.
        Assert.Contains("https://farm.example.com/dashboard/notifications", transport.Last.HtmlBody);
        Assert.Contains("https://farm.example.com/dashboard/notifications/preferences", transport.Last.HtmlBody);
        Assert.Contains("https://farm.example.com/dashboard/notifications", transport.Last.TextBody);
    }

    [Fact]
    public async Task Digest_EscapesAlertTitlesAndMessages()
    {
        var (service, transport) = Create();

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com",
            "<b>Farm</b>",
            new[] { Alert("<img src=x onerror=alert(1)>", "Barn & silo <b>check</b>") });

        var html = transport.Last.HtmlBody;

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img src=x", html);
        Assert.Contains("Barn &amp; silo &lt;b&gt;check&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Farm</b>", html);
        Assert.Contains("&lt;b&gt;Farm&lt;/b&gt;", html);
    }

    [Fact]
    public async Task Digest_SubjectCountsAlertsCorrectly()
    {
        var (service, transport) = Create();

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com", "Green Acres", new[] { Alert("Late", "One.") });

        Assert.Contains("1 new alert on", transport.Last.Subject);

        (service, transport) = Create();
        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com",
            "Green Acres",
            new[] { Alert("Late", "One."), Alert("Late", "Two.", severity: NotificationSeverity.Warning) });

        Assert.Contains("2 new alerts on", transport.Last.Subject);
    }

    [Fact]
    public async Task Digest_TextBodyCarriesEveryAlertAndItsLink()
    {
        var (service, transport) = Create(frontendBaseUrl: "https://farm.example.com");

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com",
            "Green Acres",
            new[]
            {
                Alert("3 vaccinations overdue", "Cattle need vaccinating.", "/dashboard/health/vaccinations"),
                Alert("2 tasks overdue", "Feed tasks are late.", "/dashboard/tasks", NotificationSeverity.Warning)
            });

        var text = transport.Last.TextBody;

        Assert.Contains("[Critical] 3 vaccinations overdue", text);
        Assert.Contains("[Warning] 2 tasks overdue", text);
        Assert.Contains("Cattle need vaccinating.", text);
        Assert.Contains("https://farm.example.com/dashboard/tasks", text);
    }

    [Fact]
    public async Task Digest_FallsBackToLocalhostWhenNoFrontendUrlIsConfigured()
    {
        // Documented fallback (the same default the reset/invitation links use), kept
        // visible here so the guard's warning about it has something to point at.
        var (service, transport) = Create(frontendBaseUrl: null);

        await service.SendAlertNotificationsEmailAsync(
            "manager@example.com", "Green Acres", new[] { Alert("Late", "Task overdue.", "/dashboard/tasks") });

        Assert.Contains("http://localhost:3000/dashboard/tasks", transport.Last.HtmlBody);
    }

    [Fact]
    public async Task EveryMessage_SendsBothABodyAndAnAlternative()
    {
        var (service, transport) = Create();

        await service.SendPasswordResetEmailAsync("a@example.com", "https://farm.example.com/r?token=1");
        await service.SendFarmInvitationEmailAsync("b@example.com", "Green Acres", "https://farm.example.com/i?token=2");
        await service.SendAlertNotificationsEmailAsync(
            "c@example.com", "Green Acres", new[] { Alert("Late", "Task overdue.", "/dashboard/tasks") });

        Assert.Equal(3, transport.Sent.Count);
        foreach (var message in transport.Sent)
        {
            Assert.False(string.IsNullOrWhiteSpace(message.TextBody), $"{message.Subject}: no plain-text body");
            Assert.False(string.IsNullOrWhiteSpace(message.HtmlBody), $"{message.Subject}: no HTML body");
            Assert.Contains("<html", message.HtmlBody);
            Assert.False(string.IsNullOrWhiteSpace(message.Subject));
        }
    }
}
