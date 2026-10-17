using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FMS.API.RateLimiting;

/// <summary>
/// The rate-limit policies for the credential-free demo endpoints.
///
/// Kept here rather than inline in <c>Program.cs</c> so the test host can register the exact
/// numbers production ships: a policy that only exists in the startup file would be exercised
/// by no test, and a drifting copy in the test would prove nothing about the real limit.
///
/// The policy names are the ones the <c>AuthController</c> actions reference through
/// <c>[EnableRateLimiting]</c>. Partitioning is per remote IP, so one noisy visitor cannot
/// exhaust the budget for everyone else — and, deliberately, cannot burn the global bucket.
/// </summary>
public static class DemoRateLimitPolicies
{
    /// <summary>Policy name on <c>POST /auth/demo-login</c>.</summary>
    public const string Login = "demo-login";

    /// <summary>Policy name on <c>POST /auth/demo-reset</c>.</summary>
    public const string Reset = "demo-reset";

    /// <summary>
    /// Adds both demo policies to a rate-limiter configuration. Called once from
    /// <c>Program.cs</c> and once from the demo test host.
    /// </summary>
    public static void Add(RateLimiterOptions options)
    {
        options.RejectionStatusCode = 429;

        // A scanner cannot burn the global 300/min bucket on a public demo button.
        options.AddPolicy(Login, httpContext => FixedWindow(httpContext, permitLimit: 10));

        // demo-reset is a SystemOwner action but still gated so a noisy caller cannot hammer
        // the database reset path.
        options.AddPolicy(Reset, httpContext => FixedWindow(httpContext, permitLimit: 5));
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext httpContext, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            });
}
