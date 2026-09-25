namespace FMS.Application.Common;

/// <summary>
/// The languages the API will remember for a user, and the rules for accepting one.
///
/// <para>
/// The server stores a language but never renders in it: no server-generated text is
/// translated here (the client renders message keys — see <c>X-Message-Key</c>). The one
/// exception is the digest email, which still sends English because it is composed and
/// sent outside any request, with no client to hand a key to. Storing the choice now
/// means that the day the email needs it, the value is already there.
/// </para>
///
/// <para>
/// The list mirrors <c>SUPPORTED_LOCALES</c> in <c>client/src/i18n/locale.ts</c>. The two
/// are kept in step by hand — a language the client cannot render is not worth storing,
/// and a language the server rejects can never be reached from a second device.
/// </para>
/// </summary>
public static class SupportedLocales
{
    /// <summary>Used when a user has never chosen, and by the client's own fallback.</summary>
    public const string Default = "en";

    public static readonly IReadOnlyList<string> All = new[] { "en", "es", "ar" };

    /// <summary>Longest value we will store, so an abandoned column can never hold a blob.</summary>
    public const int MaxLength = 20;

    public static bool IsSupported(string? locale) =>
        locale is not null && All.Contains(locale, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The stored form of <paramref name="locale"/>, or null when it is not one we ship.
    ///
    /// A region or script tag is reduced to the language it names first — `AR`, `ar-EG` and
    /// `ar-EG-u-nu-latn` are all Arabic — which is the same rule the client applies in
    /// `normalizeLocale`. Without it, a browser reporting `ar-EG` would be refused while the
    /// client cheerfully treats it as Arabic, so the two halves would disagree about what a
    /// supported language is.
    ///
    /// Deliberately strict about the *language*: an unknown one is a null (leave the choice
    /// alone), never a fall back to English, because silently overwriting a Spanish user's
    /// preference with English is worse than keeping what is already stored.
    /// </summary>
    public static string? Normalize(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return null;
        var trimmed = locale.Trim();
        var baseLanguage = trimmed.Split('-', '_')[0];
        return IsSupported(baseLanguage)
            ? All.First(l => string.Equals(l, baseLanguage, StringComparison.OrdinalIgnoreCase))
            : null;
    }
}
