using FMS.Application.Animal;
using Microsoft.Extensions.Configuration;

namespace FMS.Infrastructure.Animals;

/// <summary>
/// Resolves the frontend origin an animal's QR label should point at.
///
/// <para>
/// The setting is the same <c>Frontend:BaseUrl</c> the invitation and password-reset links use,
/// and the same one <c>EmailConfigurationGuard</c> refuses to start against when it is a
/// template value or missing outside Development. That is deliberate: a label is a printed
/// link, so it has exactly the same failure mode as an emailed link — with the extra cost that
/// a wrong one is glued to an animal and only discovered when somebody scans it. Reusing the
/// setting means it is the same configuration being kept right, and the same guard watching it.
/// </para>
///
/// <para>
/// The only place this is read for QR purposes, so the payload has one owner rather than one
/// per call site.
/// </para>
/// </summary>
public static class AnimalQrLinks
{
    /// <summary>
    /// The same fallback the auth and membership services use for local development, so a
    /// developer's printed labels point at their own frontend rather than at nothing.
    /// </summary>
    public const string DefaultFrontendBaseUrl = "http://localhost:3000";

    public static string FrontendBaseUrl(IConfiguration configuration) =>
        configuration["Frontend:BaseUrl"] is { Length: > 0 } configured
            ? configured
            : DefaultFrontendBaseUrl;

    /// <summary>The code's content: the absolute URL that opens one animal.</summary>
    public static string BuildUrl(IConfiguration configuration, Guid animalId) =>
        AnimalQrCode.BuildUrl(FrontendBaseUrl(configuration), animalId);
}
