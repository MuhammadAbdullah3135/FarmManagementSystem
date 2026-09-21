using System.Net;
using System.Text;
using FMS.Application.Auth;
using FMS.Application.Email;
using FMS.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace FMS.Infrastructure.Auth;

/// <summary>
/// Renders the three emails the application sends - password reset, farm invitation
/// and the alert digest - and hands them to the configured transport.
///
/// The interface and this class's name are unchanged, so no calling code or
/// dependency-injection registration moves: what changed is that the messages are now
/// composed as text and HTML and actually delivered, instead of a link being written
/// to the log. Both flows that contain a link take it as an argument and only need it
/// escaping; the digest's links are stored as frontend-relative paths, which are
/// useless in an inbox, so they are resolved against the configured frontend base URL.
///
/// Everything interpolated from the database - farm names, alert titles and messages -
/// is HTML-encoded: those strings come from user input, and an email is an HTML
/// document.
/// </summary>
public class EmailService : IEmailService
{
    private const string ApplicationName = "Farm Management System";

    /// <summary>Frontend routes the digest points at (see client/src/App.tsx).</summary>
    private const string NotificationCentrePath = "/dashboard/notifications";

    private const string NotificationPreferencesPath = "/dashboard/notifications/preferences";

    private readonly IEmailTransport _transport;
    private readonly IConfiguration _configuration;

    public EmailService(IEmailTransport transport, IConfiguration configuration)
    {
        _transport = transport;
        _configuration = configuration;
    }

    // ── password reset ────────────────────────────────────

    public Task SendPasswordResetEmailAsync(string email, string resetLink)
    {
        var subject = $"Reset your {ApplicationName} password";

        var text = $"""
            We received a request to reset the password for {email}.

            Open this link to choose a new password:
            {resetLink}

            The link can be used once and expires in one hour. If you did not ask for
            this, you can ignore this message - your password will not change.
            """;

        var html = Wrap(
            "Choose a new password",
            $"""
            <p style="margin:0 0 16px">We received a request to reset the password for <strong>{Encode(email)}</strong>.</p>
            {Button("Choose a new password", resetLink)}
            <p style="margin:16px 0 0;color:#8c8c8c;font-size:13px">
              The link can be used once and expires in one hour. If you did not ask for this,
              you can ignore this message &mdash; your password will not change.
            </p>
            """);

        return _transport.SendAsync(new EmailMessage(email, subject, text, html));
    }

    // ── farm invitation ───────────────────────────────────

    public Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink)
    {
        var displayFarmName = DisplayName(farmName, "a farm");
        var expiryDays = InvitationExpiryDays;

        var subject = $"You've been invited to join {SingleLine(displayFarmName)}";

        var text = $"""
            You have been invited to join {displayFarmName} on {ApplicationName}.

            Accept the invitation here:
            {inviteLink}

            The link can be used once and expires in {expiryDays} days. If you were not
            expecting this, you can ignore this message.
            """;

        var html = Wrap(
            $"Join {Encode(displayFarmName)}",
            $"""
            <p style="margin:0 0 16px">You have been invited to join <strong>{Encode(displayFarmName)}</strong> on {ApplicationName}.</p>
            {Button("Accept the invitation", inviteLink)}
            <p style="margin:16px 0 0;color:#8c8c8c;font-size:13px">
              The link can be used once and expires in {expiryDays} days. If you were not expecting
              this, you can ignore this message.
            </p>
            """);

        return _transport.SendAsync(new EmailMessage(email, subject, text, html));
    }

    // ── alert digest ──────────────────────────────────────

    public Task SendAlertNotificationsEmailAsync(
        string email,
        string farmName,
        IReadOnlyList<NotificationEmailItem> notifications)
    {
        var count = notifications.Count;
        var plural = count == 1 ? "" : "s";
        var displayFarmName = DisplayName(farmName, "your farm");

        var subject = $"{count} new alert{plural} on {SingleLine(displayFarmName)}";

        var text = new StringBuilder();
        text.AppendLine($"{count} new alert{plural} on {displayFarmName}:");
        text.AppendLine();
        foreach (var item in notifications)
        {
            text.AppendLine($"[{item.Severity}] {item.Title}");
            text.AppendLine($"    {item.Message}");
            if (AbsoluteUrl(item.Link) is { } link)
            {
                text.AppendLine($"    {link}");
            }

            text.AppendLine();
        }

        text.AppendLine($"Notification centre: {AbsoluteUrl(NotificationCentrePath)}");
        text.AppendLine($"Change which alerts are emailed to you: {AbsoluteUrl(NotificationPreferencesPath)}");

        var html = Wrap(
            $"{count} new alert{plural} on {Encode(displayFarmName)}",
            BuildAlertListHtml(notifications)
            + $"""
              <p style="margin:20px 0 0;font-size:13px">
                <a href="{Encode(AbsoluteUrl(NotificationCentrePath)!)}" style="color:#1677ff">Open the notification centre</a>
                &nbsp;&middot;&nbsp;
                <a href="{Encode(AbsoluteUrl(NotificationPreferencesPath)!)}" style="color:#1677ff">Change which alerts are emailed to you</a>
              </p>
              """);

        return _transport.SendAsync(new EmailMessage(email, subject, text.ToString(), html));
    }

    // ── rendering helpers ─────────────────────────────────

    private string BuildAlertListHtml(IReadOnlyList<NotificationEmailItem> notifications)
    {
        var html = new StringBuilder();

        foreach (var item in notifications)
        {
            var colour = item.Severity switch
            {
                NotificationSeverity.Critical => "#cf1322",
                NotificationSeverity.Warning => "#d46b08",
                _ => "#0958d9"
            };

            html.AppendLine($"""
                <div style="border-left:3px solid {colour};padding:2px 0 2px 12px;margin:0 0 14px">
                  <p style="margin:0 0 4px;font-weight:600">{Encode(item.Severity)}: {Encode(item.Title)}</p>
                  <p style="margin:0;color:#595959">{Encode(item.Message)}</p>
                </div>
                """);

            if (AbsoluteUrl(item.Link) is { } link)
            {
                html.AppendLine($"""
                    <p style="margin:-10px 0 16px 12px;font-size:13px">
                      <a href="{Encode(link)}" style="color:#1677ff">{Encode(link)}</a>
                    </p>
                    """);
            }
        }

        return html.ToString();
    }

    /// <summary>The common shell, so the three messages look like they came from one application.</summary>
    private static string Wrap(string heading, string bodyHtml) => $"""
        <!DOCTYPE html>
        <html>
          <body style="margin:0;padding:24px;background:#fafafa;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#262626">
            <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:8px;padding:24px">
              <h1 style="margin:0 0 16px;font-size:20px;line-height:1.3">{heading}</h1>
              {bodyHtml}
            </div>
            <p style="max-width:520px;margin:16px auto 0;color:#8c8c8c;font-size:12px">
              {ApplicationName}
            </p>
          </body>
        </html>
        """;

    /// <summary>
    /// A call-to-action link, plus the URL in plain text: some clients strip or mangle
    /// anchors, and a password reset that cannot be clicked still has to be usable.
    /// </summary>
    private static string Button(string label, string url) => $"""
        <p style="margin:0">
          <a href="{Encode(url)}" style="display:inline-block;padding:10px 18px;background:#1677ff;color:#ffffff;border-radius:6px;text-decoration:none">{Encode(label)}</a>
        </p>
        <p style="margin:12px 0 0;color:#8c8c8c;font-size:12px;word-break:break-all">
          Or paste this link into your browser:<br />{Encode(url)}
        </p>
        """;

    /// <summary>
    /// Resolves a stored link for an inbox. Alert links are frontend-relative
    /// ("/dashboard/tasks"); an absolute link is left alone. Returns null for no link,
    /// so callers can omit the line entirely rather than render an empty anchor.
    /// </summary>
    private string? AbsoluteUrl(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        return $"{FrontendBaseUrl}/{link.TrimStart('/')}";
    }

    /// <summary>Same fallback the reset/invitation callers use when building their links.</summary>
    private string FrontendBaseUrl =>
        _configuration["Frontend:BaseUrl"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : "http://localhost:3000";

    /// <summary>
    /// The invitation lifetime the email quotes. Read from the same setting
    /// FarmMembershipService gives the token, so the promise and the expiry cannot
    /// drift apart.
    /// </summary>
    private int InvitationExpiryDays =>
        int.TryParse(_configuration["Invitations:ExpiryDays"], out var days) ? days : 7;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>A subject is one header line, so anything with line breaks is flattened.</summary>
    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string DisplayName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
