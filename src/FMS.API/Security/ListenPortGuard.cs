namespace FMS.API.Security;

/// <summary>
/// Decides whether the process may serve on the port named by the <c>PORT</c> environment
/// variable.
///
/// Why this exists: <c>PORT</c> is honoured by clearing every configured URL and binding
/// <c>http://+:{PORT}</c> — it outranks <c>ASPNETCORE_URLS</c>, <c>--urls</c> and the launch
/// profile. That is what a platform such as Heroku wants, and it is correct there. In
/// Development it is a trap, because the Vite dev server proxies <c>/api</c> to
/// <c>http://localhost:5200</c> and nothing else. A <c>PORT</c> inherited from the shell —
/// <c>PORT=0</c> is the one that bit us, since it asks Kestrel for a random free port — moves
/// the API somewhere the client never looks, and nothing in either log says so: the API is
/// healthy, every request is proxied to a port with no listener, and the UI shows empty lists.
///
/// So Development refuses any other value instead of binding it. Outside Development the
/// platform owns <c>PORT</c>, and the proxy contract does not apply.
/// </summary>
public static class ListenPortGuard
{
    /// <summary>
    /// The one port the client's dev proxy forwards to. Kept here as the single source of
    /// truth for the message, the comparison and the test.
    /// </summary>
    public const string DevelopmentPort = "5200";

    /// <summary>
    /// Returns a human-readable problem, or null when the port may be used in this
    /// environment. Never throws.
    /// </summary>
    public static string? Validate(string? port, bool isDevelopment)
    {
        // Unset is fine: the `http` launch profile's applicationUrl binds 5200, and a
        // platform that sets PORT is not running in Development.
        if (!isDevelopment || string.IsNullOrWhiteSpace(port))
        {
            return null;
        }

        var trimmed = port.Trim();

        if (string.Equals(trimmed, DevelopmentPort, StringComparison.Ordinal))
        {
            return null;
        }

        return $"PORT is set to '{trimmed}', but the API serves on {DevelopmentPort} in Development.";
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when Development must not serve on this
    /// port, with a message that names the value, the consequence and the fix.
    /// </summary>
    public static void EnsureUsable(string? port, string environmentName, bool isDevelopment)
    {
        var problem = Validate(port, isDevelopment);
        if (problem is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start in the '{environmentName}' environment. {problem} "
            + $"The Vite dev server proxies /api to http://localhost:{DevelopmentPort}, and PORT overrides "
            + "every other URL setting (ASPNETCORE_URLS, --urls and the launch profile), so any other value "
            + "binds a port the client never looks at — PORT=0 asks Kestrel for a random free port — and the "
            + "API then looks healthy while every proxied request fails. "
            + $"Unset PORT, or run with PORT={DevelopmentPort}.");
    }
}
