using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FMS.API.Health;

/// <summary>
/// The body <c>/health</c> answers with.
/// </summary>
/// <remarks>
/// Extracted from the endpoint's inline lambda so the contract other things depend on can be
/// asserted in-process: the post-deploy smoke check reads <c>build.sha</c> from this response to
/// decide whether the release it just made is the one serving traffic, and a field that silently
/// stops being written would otherwise only be noticed by that check failing in production —
/// the one place it is hardest to tell a wiring mistake from a broken deploy.
/// </remarks>
public static class HealthPayload
{
    public static object For(HealthReport report, string buildSha, string environment) => new
    {
        status = report.Status.ToString(),
        build = new
        {
            sha = buildSha,
            environment
        },
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            duration = entry.Value.Duration.TotalMilliseconds,
            description = entry.Value.Description,
            exception = entry.Value.Exception?.Message
        }),
        duration = report.TotalDuration.TotalMilliseconds
    };
}
