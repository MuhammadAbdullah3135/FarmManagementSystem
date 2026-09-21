using FMS.Application.Email;
using Microsoft.Extensions.Configuration;

namespace FMS.API.Security;

/// <summary>
/// Validates the email configuration before the host starts.
///
/// The failure this prevents is the quiet one: with <c>Email:Provider = SendGrid</c>
/// and no API key or sender, every password reset and invitation is accepted, logged
/// as sent, and never delivered - and because those flows deliberately keep working
/// when a send fails, nothing surfaces. So an unusable provider configuration is a
/// startup failure outside Development, in the same spirit as the JWT signing-key
/// guard: refuse to run rather than degrade silently.
///
/// Inside Development it downgrades to the Log transport and returns a warning, so a
/// half-configured checkout still runs and still lets you complete a reset from the
/// log. The warning is logged once the host exists, the same shape as the local
/// file-storage warning in Program.cs.
/// </summary>
public static class EmailConfigurationGuard
{
    /// <summary>
    /// Throws when real delivery is selected but cannot work. Inside Development it
    /// falls back to the Log transport (mutating <paramref name="options"/>) and
    /// returns the warning to log once the host is built. Returns null when there is
    /// nothing an operator needs to be told.
    /// </summary>
    public static string? EnsureUsable(
        EmailOptions options,
        IConfiguration configuration,
        string environmentName,
        bool isDevelopment)
    {
        var problem = Validate(options);

        if (problem is not null)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"Refusing to start in the '{environmentName}' environment. {problem} "
                    + "Supply the missing settings (for example Email__FromAddress and Email__SendGrid__ApiKey), "
                    + "or set Email__Provider=Log to run without sending mail. "
                    + "As configured, nobody would receive a password reset or an invitation.");
            }

            options.Provider = EmailProviderKind.Log;

            return $"{problem} Falling back to the Log transport in Development: "
                + "messages will be written to the log, not sent.";
        }

        var warnings = new List<string>();

        if (options.Provider == EmailProviderKind.Log && !isDevelopment)
        {
            warnings.Add(
                "Email delivery is not configured (Email:Provider = Log), so password resets and "
                + "invitations are written to the log instead of being sent.");
        }

        var frontendWarning = FrontendUrlWarning(configuration);
        if (frontendWarning is not null)
        {
            warnings.Add(frontendWarning);
        }

        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    /// <summary>Returns what is wrong with the configured provider, or null when it can work.</summary>
    public static string? Validate(EmailOptions options)
    {
        if (options.Provider == EmailProviderKind.Log)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            return "Email:FromAddress is not configured.";
        }

        if (!LooksLikeAnAddress(options.FromAddress))
        {
            return $"Email:FromAddress ('{options.FromAddress}') is not a valid email address.";
        }

        if (options.Provider == EmailProviderKind.SendGrid && string.IsNullOrWhiteSpace(options.SendGrid.ApiKey))
        {
            return "Email:SendGrid:ApiKey is not configured.";
        }

        return null;
    }

    /// <summary>
    /// Every email contains a link back to the frontend, so a template base URL
    /// produces messages whose links go somewhere that does not serve this app. Warn
    /// rather than fail: the application is otherwise working, and refusing to boot
    /// over a link would be the wrong trade.
    /// </summary>
    private static string? FrontendUrlWarning(IConfiguration configuration)
    {
        var baseUrl = configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "Frontend:BaseUrl is not configured, so links in emails will point at "
                + "http://localhost:3000 and go nowhere for a recipient.";
        }

        if (baseUrl.Contains("yourdomain.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"Frontend:BaseUrl is still the template value ('{baseUrl}'), so every link in every "
                + "email will point at a host that does not serve this application. Set Frontend__BaseUrl to "
                + "the real frontend URL, including any base path.";
        }

        return null;
    }

    /// <summary>
    /// Deliberately loose: providers are the authority on deliverable addresses, and
    /// rejecting a valid-but-unusual one at startup would be worse than forwarding it.
    /// </summary>
    private static bool LooksLikeAnAddress(string value)
    {
        if (value.Contains(' ') || value.Contains('\n') || value.Contains('\r'))
        {
            return false;
        }

        var at = value.IndexOf('@');
        return at > 0
            && at < value.Length - 1
            && value.IndexOf('.', at) > at + 1
            && !value.EndsWith('.');
    }
}
