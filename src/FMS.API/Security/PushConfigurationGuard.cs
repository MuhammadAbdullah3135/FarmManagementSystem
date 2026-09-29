using FMS.Application.Notifications;
using FMS.Infrastructure.Notifications;

namespace FMS.API.Security;

/// <summary>
/// Validates the push configuration before the host starts.
///
/// <para>
/// The failure this prevents is the quiet one, and it is worse here than for email.
/// With <c>Push:Enabled=true</c> and keys that are missing, malformed, or from two
/// different pairs, every push is refused by the push service — and nothing in the
/// product looks broken: the notifications are still written, the preferences screen
/// still offers its switches, and the only symptom is a phone that never buzzes. So an
/// unusable push configuration is a startup failure outside Development, in the same
/// spirit as the JWT signing-key and email guards: refuse to run rather than degrade
/// silently.
/// </para>
///
/// <para>
/// Inside Development it turns the channel off and returns a warning, so a checkout
/// with no keys still runs (and still passes every test that does not involve a real
/// device).
/// </para>
///
/// <para>
/// Push being *off* is not a problem to fix — it is the default, and a deployment that
/// never wants push should not have to argue with a guard. Outside Development it is
/// still worth saying once, because a farm that expected alerts on a phone would
/// otherwise discover it at the wrong moment.
/// </para>
/// </summary>
public static class PushConfigurationGuard
{
    /// <summary>
    /// Turns push off (mutating <paramref name="options"/>) or throws, and returns what an
    /// operator should be told. Returns null when there is nothing to say.
    /// </summary>
    public static string? EnsureUsable(PushOptions options, string environmentName, bool isDevelopment)
    {
        if (!options.Enabled)
        {
            return isDevelopment
                ? null
                : "Push notifications are turned off (Push:Enabled is false), so no alert reaches a "
                    + "phone. Set Push__Enabled=true with a VAPID key pair to enable them.";
        }

        var problem = Validate(options);

        if (problem is not null)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"Refusing to start in the '{environmentName}' environment. {problem} "
                    + "Supply Push__PublicKey, Push__PrivateKey and Push__Subject (or set "
                    + "Push__Enabled=false to run without push notifications). "
                    + "As configured, every notification would be written and none would reach a device.");
            }

            options.Enabled = false;

            return $"{problem} Turning push off in Development: notifications stay in-app only.";
        }

        return null;
    }

    /// <summary>Returns what is wrong with the configured keys, or null when they can work.</summary>
    public static string? Validate(PushOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Subject))
        {
            return "Push:Subject is not configured.";
        }

        // RFC 8292 §2 requires a "sub" that a push service operator can use to contact
        // whoever is sending: a mailto: or https: URI, and nothing else. Sent as plain text
        // it is either ignored or the whole request is rejected, depending on the service.
        if (!options.Subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !options.Subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"Push:Subject ('{options.Subject}') must start with 'mailto:' or 'https://'.";
        }

        if (!VapidKeys.TryParse(options.PublicKey, options.PrivateKey, out _, out var keyProblem))
        {
            return keyProblem;
        }

        return null;
    }
}
