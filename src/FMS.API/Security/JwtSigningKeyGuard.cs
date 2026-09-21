using System.Text;

namespace FMS.API.Security;

/// <summary>
/// Decides whether a configured JWT signing key may be used in this environment.
///
/// Why this exists: the development key is deliberately committed so a fresh
/// checkout runs with no setup, and the API reads <c>Jwt:SecretKey</c> with a
/// null-forgiving operator. A deployment that forgot to set
/// <c>Jwt__SecretKey</c> therefore used to boot happily and sign every access
/// and refresh token with a value that is public in this repository — and a JWT
/// signed with a public key is not a weak credential, it is no credential: any
/// reader of the repository can mint a SystemOwner token.
///
/// So the key is validated once, before any service is registered, and the
/// process refuses to start in non-Development environments rather than
/// degrading silently. Development is allowed to use the committed key, which
/// keeps the out-of-the-box developer experience intact.
/// </summary>
public static class JwtSigningKeyGuard
{
    /// <summary>
    /// The committed development-only key. Kept here as the single source of
    /// truth so the value the guard rejects and the value in
    /// <c>appsettings.Development.json</c> cannot drift apart unnoticed
    /// (a test asserts the two match).
    /// </summary>
    public const string DevelopmentPlaceholderKey =
        "DEV-ONLY-INSECURE-JWT-SIGNING-KEY-NOT-FOR-ANY-OTHER-ENVIRONMENT";

    /// <summary>
    /// HS256 keys shorter than the hash output weaken the signature, and
    /// Microsoft.IdentityModel rejects anything under 128 bits outright - so a
    /// short key is a startup failure somewhere further down with a much less
    /// useful message. 32 bytes (256 bits) is the honest minimum for HS256.
    /// </summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// Returns a human-readable problem, or null when the key is usable in this
    /// environment. Never throws.
    /// </summary>
    public static string? Validate(string? key, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Jwt:SecretKey is not configured.";
        }

        if (key == DevelopmentPlaceholderKey)
        {
            // Acceptable only in Development, where it is the intended value.
            return isDevelopment
                ? null
                : "Jwt:SecretKey is still the development placeholder committed to this repository. "
                  + "Anyone with the repository can forge a token, including a SystemOwner one.";
        }

        if (Encoding.UTF8.GetByteCount(key) < MinimumKeyBytes)
        {
            return $"Jwt:SecretKey must be at least {MinimumKeyBytes} bytes long for HS256; "
                + $"the configured value is {Encoding.UTF8.GetByteCount(key)} bytes.";
        }

        return null;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the key must not be
    /// used in the given environment, with a message that names the exact fix.
    /// </summary>
    public static void EnsureUsable(string? key, string environmentName, bool isDevelopment)
    {
        var problem = Validate(key, isDevelopment);
        if (problem is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start in the '{environmentName}' environment. {problem} "
            + "Generate a key with `openssl rand -base64 48` and supply it as the Jwt__SecretKey "
            + "environment variable (Heroku: `heroku config:set Jwt__SecretKey=...`), then restart. "
            + "Rotating it invalidates every previously issued access and refresh token.");
    }
}
