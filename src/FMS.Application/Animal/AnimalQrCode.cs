namespace FMS.Application.Animal;

/// <summary>
/// The grammar of an animal's QR code: the one place the payload's shape is defined.
///
/// <para>
/// **Why the payload is a URL and not the tag number.** A printed ear-tag label is the
/// longest-lived artifact this system produces, and <c>Animal.TagNumber</c> is editable
/// (<see cref="UpdateAnimalRequest"/>) and only unique among the farm's non-deleted animals.
/// A label anchored to it can therefore come to identify a different animal with nobody
/// changing anything physical. The animal's <c>Id</c> cannot: it is immutable for the life of
/// the record. Human readability is not lost — the label sheet prints the tag number as text
/// beside the code — and the URL shape means a stock camera app can also open the record.
/// </para>
///
/// <para>
/// **Why the app's base path is not in the grammar.** The code is read by matching the route
/// segment, not by comparing against a configured prefix: a label printed by the deployed app
/// must still scan in a build served from a different path (and vice versa), and a device that
/// scans a code is not the place to discover that a deployment moved. Only the segment matters,
/// and it is a fixed part of the SPA's routes.
/// </para>
///
/// <para>
/// **Why there is no farm id in the payload.** A label belongs to an animal, not to a farm
/// selection. Resolution is always against the *scanning* user's active farm, so another farm's
/// code resolves to nothing rather than taking the reader across a farm boundary — the same
/// rule the rest of the API enforces by scoping every read.
/// </para>
/// </summary>
public static class AnimalQrCode
{
    /// <summary>
    /// The SPA route an animal's code resolves to: <c>/dashboard/animals/{id}</c>.
    /// Kept in one place so the builder and the reader cannot disagree about it.
    /// </summary>
    public const string RouteMarker = "/dashboard/animals/";

    /// <summary>
    /// Builds the absolute payload that goes into an animal's code.
    ///
    /// The base URL is the configured frontend origin (the same setting the email links use),
    /// so a label printed from this deployment points at this deployment.
    /// </summary>
    public static string BuildUrl(string frontendBaseUrl, Guid animalId)
    {
        var baseUrl = (frontendBaseUrl ?? string.Empty).TrimEnd('/');
        return $"{baseUrl}{RouteMarker}{animalId}";
    }

    /// <summary>
    /// Reads the animal id out of a scanned code, or returns false for anything that is not one.
    ///
    /// Deliberately strict: the route marker must be present and exactly one segment must follow
    /// it, parseable as a GUID. A query string or fragment is tolerated (a reader that appends
    /// one is not a different label) but a longer path is not — <c>…/animals/{id}/weights</c> is a
    /// different address, and accepting it would mean guessing which part was the identity.
    ///
    /// This is the reader the device-side resolver mirrors, so a change here is a change to the
    /// client's parser too — see <c>client/src/offline/scanResolve.ts</c> and the tests on both
    /// sides, which assert the two agree about the accepted shape.
    /// </summary>
    public static bool TryReadAnimalId(string? scanned, out Guid animalId)
    {
        animalId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(scanned)) return false;

        var text = scanned.Trim();

        var cut = text.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) text = text[..cut];

        var marker = text.IndexOf(RouteMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;

        var idPart = text[(marker + RouteMarker.Length)..].Trim('/');
        if (idPart.Length == 0 || idPart.Contains('/')) return false;

        return Guid.TryParse(idPart, out animalId);
    }
}
