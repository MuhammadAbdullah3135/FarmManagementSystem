using Microsoft.Extensions.Configuration;

namespace FMS.API;

/// <summary>
/// Identifies the build that is actually serving traffic.
/// </summary>
/// <remarks>
/// The SPA has been stamped with its commit for a while — <c>VITE_BUILD_SHA</c> is baked into
/// the bundle and shown in the UI as "Build 9833a7c". The API had no equivalent, so a release
/// that never reached Heroku was invisible: two CI runs went red in <c>test-backend</c>, the
/// <c>deploy</c> job that waits on it never ran, and production kept answering with the old
/// container while the commit sat on GitHub. Finding that out required pushing at production by
/// hand.
///
/// The Dockerfile bakes the commit in (<c>ARG GIT_SHA</c> → <c>ENV FMS_BUILD_SHA</c>) and
/// <c>/health</c> reports it, so the post-deploy smoke check can compare the build answering
/// requests with the commit it just released — and a red deploy job can no longer hide behind a
/// green repository.
/// </remarks>
public static class BuildStamp
{
    /// <summary>The environment variable the Docker build sets, read through configuration.</summary>
    public const string ConfigurationKey = "FMS_BUILD_SHA";

    /// <summary>
    /// Reported when the image was built without the argument. Deliberately a value rather than
    /// an empty string or null: an unstamped build has to be visibly unstamped, because a check
    /// that treats "no stamp" as "fine" is not a check.
    /// </summary>
    public const string Unstamped = "unknown";

    public static string Sha(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration[ConfigurationKey])
            ? Unstamped
            : configuration[ConfigurationKey]!.Trim();
}
