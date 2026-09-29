using System.Security.Cryptography;
using System.Text;
using FMS.Application.Notifications;

namespace FMS.Infrastructure.Notifications;

/// <summary>
/// This deployment's VAPID identity (RFC 8292): the P-256 key pair that push
/// services use to attribute a message to an application server.
///
/// <para>
/// Two things are needed from it and they are needed in different places. The
/// <em>public</em> half is handed to browsers as <c>applicationServerKey</c> when
/// they subscribe (so <c>GET …/notifications/push</c> serves it), and the
/// <em>private</em> half signs the short-lived JWT that authorises each POST.
/// A push service that cannot verify that JWT answers 401 and delivers nothing,
/// which is why an unusable pair is caught at startup rather than at the first alert
/// (see <c>PushConfigurationGuard</c>).
/// </para>
///
/// <para>
/// The key material is the same shape the Web Push ecosystem uses — a 65-octet
/// uncompressed point for the public half (either base64url directly, or the raw
/// <c>04||X||Y</c> bytes), and the 32-octet scalar for the private half. That is what
/// <c>web-push generate-vapid-keys</c> and <c>openssl ecparam</c> produce, so an
/// operator can mint a pair with either tool.
/// </para>
/// </summary>
public sealed class VapidKeys
{
    private const int CoordinateLength = 32;
    private const int PrivateKeyLength = 32;

    /// <summary>An uncompressed P-256 point: one <c>0x04</c> octet and two 32-octet coordinates.</summary>
    public const int PublicKeyLength = 1 + (2 * CoordinateLength);

    private VapidKeys(byte[] publicKey, byte[] privateKey, ECDsa signing)
    {
        PublicKey = publicKey;
        PrivateKey = privateKey;
        Signing = signing;
        PublicKeyBase64Url = Base64Url.Encode(publicKey);
    }

    /// <summary>The 65-octet uncompressed public point.</summary>
    public byte[] PublicKey { get; }

    /// <summary>The 32-octet private scalar. Never logged.</summary>
    public byte[] PrivateKey { get; }

    /// <summary>Ready for the <c>k=</c> parameter of the Authorization header.</summary>
    public string PublicKeyBase64Url { get; }

    /// <summary>The signer, holding the same private scalar.</summary>
    public ECDsa Signing { get; }

    /// <summary>
    /// Parses a configured pair, verifying that the two halves actually belong
    /// together.
    ///
    /// <para>
    /// The cross-check matters: a deployment that pastes a public key from one pair
    /// and a private key from another produces a JWT the public key cannot verify,
    /// and the failure lands on a farmer's phone as silence. Here it is a startup
    /// error. A public key that is invalid on the curve is rejected for the same
    /// reason RFC 8291 §7 gives receivers: an unchecked point can leak the private
    /// key.
    /// </para>
    /// </summary>
    public static bool TryParse(string? publicKey, string? privateKey, out VapidKeys? keys, out string? problem)
    {
        keys = null;
        problem = null;

        if (!Base64Url.TryDecode(privateKey, PrivateKeyLength, out var privateBytes)
            && !TryDecodePublicPoint(privateKey, out privateBytes))
        {
            problem = "Push:PrivateKey is not a base64url P-256 private scalar (32 octets).";
            return false;
        }

        byte[] publicBytes;
        if (TryDecodePublicPoint(publicKey, out publicBytes))
        {
            if (!IsOnCurve(publicBytes))
            {
                problem = "Push:PublicKey is not a valid P-256 point.";
                return false;
            }
        }
        else if (string.IsNullOrWhiteSpace(publicKey))
        {
            // Deriving the public half from the private one is a convenience for a
            // deployment that supplies only the secret; the private half is the whole
            // identity, and handing a browser an unset applicationServerKey would fail
            // at subscribe time with nothing to read in the server's logs.
            publicBytes = Array.Empty<byte>();
        }
        else
        {
            problem = "Push:PublicKey is not a base64url uncompressed P-256 point (65 octets).";
            return false;
        }

        ECDsa signing;
        try
        {
            signing = ECDsa.Create(ImportPrivate(privateBytes));
        }
        catch (CryptographicException ex)
        {
            problem = $"Push:PrivateKey could not be imported: {ex.Message}";
            return false;
        }

        if (publicBytes.Length == 0)
        {
            var derived = signing.ExportParameters(false).Q;
            publicBytes = EncodePoint(derived.X!, derived.Y!);
        }
        else if (!SamePoint(signing, publicBytes))
        {
            signing.Dispose();
            problem = "Push:PublicKey does not belong to Push:PrivateKey.";
            return false;
        }

        keys = new VapidKeys(publicBytes, privateBytes, signing);
        return true;
    }

    /// <summary>Creates a fresh pair. Used by tests and by the documented key-minting path.</summary>
    public static VapidKeys Generate()
    {
        using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = generated.ExportParameters(true);

        var publicKey = EncodePoint(parameters.Q.X!, parameters.Q.Y!);
        return new VapidKeys(publicKey, parameters.D!, generated);
    }

    /// <summary>
    /// The Authorization header value for one endpoint, or null when the endpoint is
    /// not a usable URL.
    ///
    /// <para>
    /// The JWT's <c>aud</c> is the endpoint's origin and its <c>exp</c> is twelve hours
    /// out — the maximum RFC 8292 allows. A token is signed per send rather than cached
    /// because signing costs microseconds and a cached token is one more thing that can
    /// be wrong only in production.
    /// </para>
    /// </summary>
    public string? BuildAuthorizationHeader(string endpoint, string subject)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var header = Base64Url.Encode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));

        var claims = Base64Url.Encode(Encoding.UTF8.GetBytes(
            $"{{\"aud\":\"{uri.Scheme}://{uri.Authority}\","
            + $"\"exp\":{DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()},"
            + $"\"sub\":\"{subject}\"}}"));

        var signingInput = $"{header}.{claims}";

        // P1363 (r||s) is what JWS ES256 specifies; the .NET default is DER, and a
        // push service given DER rejects the token as malformed.
        var signature = Signing.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"vapid t={signingInput}.{Base64Url.Encode(signature)},k={PublicKeyBase64Url}";
    }

    private static ECParameters ImportPrivate(byte[] privateKey)
    {
        // Q is derived by the runtime: the scalar is the whole key, and callers that
        // have the public half cross-check it separately.
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey
        };

        parameters.Validate();
        return parameters;
    }

    private static bool TryDecodePublicPoint(string? value, out byte[] point)
    {
        if (Base64Url.TryDecode(value, PublicKeyLength, out point))
        {
            return point[0] == 0x04;
        }

        point = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// True when the point is a solution to the curve equation, which is the check
    /// RFC 8291 requires before agreeing a secret with it.
    ///
    /// Delegated to <see cref="P256.IsOnCurve"/> rather than to
    /// <see cref="ECDiffieHellman.ImportParameters(ECParameters)"/>: the runtime accepts a
    /// fabricated point whose coordinates are merely in range.
    /// </summary>
    private static bool IsOnCurve(byte[] point) => P256.IsOnCurve(point);

    /// <summary>True when the point is the one the private scalar belongs to.</summary>
    private static bool SamePoint(ECDsa signing, byte[] point)
    {
        var q = signing.ExportParameters(false).Q;
        var expected = EncodePoint(q.X!, q.Y!);
        return CryptographicOperations.FixedTimeEquals(expected, point);
    }

    private static byte[] EncodePoint(byte[] x, byte[] y) => P256.Encode(x, y);
}
