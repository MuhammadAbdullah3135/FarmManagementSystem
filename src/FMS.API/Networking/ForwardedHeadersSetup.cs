using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FMS.API.Networking;

/// <summary>
/// Opt-in forwarded-header support for deployments behind a reverse proxy (Heroku's router,
/// a load balancer, etc.).
///
/// Why this exists: the demo rate-limit policies partition on
/// <c>HttpContext.Connection.RemoteIpAddress</c>. Behind a proxy that address is the proxy, so
/// every visitor would share one bucket (10 demo logins/minute total) and the public demo would
/// stop working almost immediately. <c>UseForwardedHeaders</c> replaces it with the address the
/// proxy reports in <c>X-Forwarded-For</c>.
///
/// Why it is gated: trusting a header is only safe when the app is not directly reachable. The
/// middleware is therefore registered only when <c>ForwardedHeaders:Enabled=true</c>, so an
/// existing deployment that is directly exposed keeps its current behaviour and does not start
/// trusting a spoofable header.
///
/// Configuration (<c>ForwardedHeaders</c> section):
/// <list type="bullet">
///   <item><description><c>Enabled</c> — register the middleware at all (default false).</description></item>
///   <item><description><c>TrustAllProxies</c> — accept any proxy address. Appropriate on Heroku,
///   where the dyno is only reachable through the router and the router's addresses are not
///   stable. Default false.</description></item>
///   <item><description><c>KnownProxies</c> / <c>KnownNetworks</c> — explicit allowlist used when
///   <c>TrustAllProxies</c> is false, in addition to ASP.NET Core's loopback default.</description></item>
///   <item><description><c>ForwardLimit</c> — how many proxy hops to walk (default 1, matching a
///   single Heroku router hop).</description></item>
/// </list>
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>True when this deployment opted into forwarded-header processing.</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetSection(SectionName).GetValue<bool>("Enabled");

    /// <summary>
    /// Builds the options <c>UseForwardedHeaders</c> should run with. Only the client address
    /// and scheme are taken from the proxy; the host header is deliberately left alone, so a
    /// spoofed <c>X-Forwarded-Host</c> cannot rewrite generated links.
    /// </summary>
    public static ForwardedHeadersOptions CreateOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        var trustAllProxies = section.GetValue<bool>("TrustAllProxies");
        if (trustAllProxies)
        {
            // ASP.NET Core trusts loopback only by default. A managed proxy is not loopback and
            // its addresses are not stable, so the allowlist is emptied here. This is safe only
            // because the deployment opted in and the origin is not directly reachable.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
        else
        {
            foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
            {
                if (IPAddress.TryParse(proxy, out var address))
                    options.KnownProxies.Add(address);
            }

            foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            {
                if (Microsoft.AspNetCore.HttpOverrides.IPNetwork.TryParse(network, out var parsed))
                    options.KnownNetworks.Add(parsed);
            }
        }

        var forwardLimit = section.GetValue<int?>("ForwardLimit");
        if (forwardLimit is > 0)
            options.ForwardLimit = forwardLimit;

        return options;
    }
}
