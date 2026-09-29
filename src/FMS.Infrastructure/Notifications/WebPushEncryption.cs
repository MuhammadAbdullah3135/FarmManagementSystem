using System.Security.Cryptography;
using System.Text;

namespace FMS.Infrastructure.Notifications;

/// <summary>
/// Encrypts one push payload for one subscription, exactly as RFC 8291 ("Message
/// Encryption for Web Push") and RFC 8188 (<c>aes128gcm</c>) specify.
///
/// <para>
/// Hand-written rather than taken from a package, for the reason the offline service
/// worker is hand-written: this is the whole of what the feature needs from another
/// library — one ECDH agreement, two HKDF chains and one AES-GCM record — and a
/// dependency here would be a second thing to keep working in exchange for about a
/// hundred lines. It is also the part most worth being able to read: it is the only
/// code in the system that handles a browser's private-adjacent key material.
/// </para>
///
/// <para>
/// The correctness claim is not "it looks like the RFC". <c>WebPushEncryptionTests</c>
/// encrypts the RFC's own example with the RFC's own keys and asserts the resulting
/// body is byte-for-byte the body in §5, and pins each intermediate value from
/// Appendix A. A browser is the only other thing that can accept a message, and a
/// deployment cannot test that per build.
/// </para>
///
/// <para>
/// What it deliberately does not do: multiple records (RFC 8291 §4 forbids it for
/// push), compression (also forbidden), and any notion of a channel or a peer — the
/// push service is an intermediary that is expected to see nothing.
/// </para>
///
/// <para>
/// The record is sealed with <b>no associated data</b>, per RFC 8188 §2: the header is
/// not authenticated, so nothing in it may be trusted further than the push service
/// that carried it (RFC 8291 §7). The header is still generated here, and its layout is
/// asserted against the RFC example, because it is what a receiver needs to derive the
/// same keys.
/// </para>
/// </summary>
public static class WebPushEncryption
{
    /// <summary>The only content coding a push message may use.</summary>
    public const string ContentEncoding = "aes128gcm";

    /// <summary>
    /// RFC 8188's suggested record size, and what RFC 8291's example uses. Any value
    /// larger than the padded plaintext plus the tag is valid; a receiver is allowed to
    /// ignore it, and every real one does.
    /// </summary>
    public const int RecordSize = 4096;

    /// <summary>The largest payload that fits one record: 4096 − header(86) − pad(1) − tag(16).</summary>
    public const int MaxPayloadLength = RecordSize - (SaltLength + 4 + 1 + VapidKeys.PublicKeyLength) - 1 - TagLength;

    private const int CoordinateLength = 32;
    private const int SaltLength = 16;
    private const int TagLength = 16;
    private const int KeyLength = 16;
    private const int NonceLength = 12;

    /// <summary>The padding delimiter RFC 8188 puts after the plaintext.</summary>
    private const byte PaddingDelimiter = 0x02;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] ContentEncryptionKeyInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>
    /// The HTTP body for one message: the RFC 8188 header, the ciphertext and the tag.
    ///
    /// <para>
    /// The ephemeral key pair and the salt are generated here and thrown away with the
    /// call: a subscription that received the same salt twice would have its nonce
    /// reused, which is the one way to break AES-GCM.
    /// </para>
    /// </summary>
    public static byte[] Encrypt(string p256dh, string authSecret, ReadOnlySpan<byte> payload) =>
        Encrypt(p256dh, authSecret, payload, salt: null, ephemeralPrivateKey: null);

    /// <summary>
    /// The same encryption with the two random inputs supplied.
    ///
    /// <para>
    /// Exists for one caller: the RFC vector test, which cannot assert a known
    /// ciphertext unless it can fix the salt and the ephemeral key the RFC's example
    /// used. Nothing in the delivery path calls it — a caller that passed a constant
    /// salt would be reusing a nonce.
    /// </para>
    /// </summary>
    internal static byte[] Encrypt(
        string p256dh,
        string authSecret,
        ReadOnlySpan<byte> payload,
        byte[]? salt,
        byte[]? ephemeralPrivateKey)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"A push payload may not exceed {MaxPayloadLength} octets; this one is {payload.Length}.");
        }

        var receiverPublicKey = DecodeReceiverKey(p256dh);
        var receiverAuthSecret = DecodeAuthSecret(authSecret);

        var (sharedSecret, senderPublicKey) = AgreeWithReceiver(receiverPublicKey, ephemeralPrivateKey);
        var keys = DeriveKeys(sharedSecret, receiverAuthSecret, receiverPublicKey, senderPublicKey, salt);

        var header = BuildHeader(keys.Salt, senderPublicKey);

        // The delimiter is appended to the plaintext (RFC 8188 §2): it is what tells a
        // receiver that a record is complete, and RFC 8291 §4 requires it to be checked.
        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext);
        plaintext[^1] = PaddingDelimiter;

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using (var aes = new AesGcm(keys.ContentEncryptionKey, TagLength))
        {
            // No associated data. RFC 8188 §2 is explicit — "the additional data passed
            // to each invocation of AEAD_AES_128_GCM is a zero-length octet sequence" —
            // and the header is therefore not covered by the tag. That is why RFC 8291 §7
            // tells a receiver to treat header fields as having come from the push
            // service: nothing in them is authenticated.
            //
            // Passing the header here instead is a mistake that hides well: the
            // ciphertext is unaffected, so a message still decrypts with a receiver that
            // makes the same mistake, and only the tag differs. The RFC vector is what
            // caught it.
            aes.Encrypt(keys.Nonce, plaintext, ciphertext, tag);
        }

        var body = new byte[header.Length + ciphertext.Length + tag.Length];
        header.CopyTo(body, 0);
        ciphertext.CopyTo(body, header.Length);
        tag.CopyTo(body, header.Length + ciphertext.Length);

        CryptographicOperations.ZeroMemory(keys.ContentEncryptionKey);
        CryptographicOperations.ZeroMemory(sharedSecret);

        return body;
    }

    /// <summary>
    /// The ECDH agreement itself: the sender's half of the key exchange, and the secret
    /// both sides can compute.
    ///
    /// <para>
    /// Separated from <see cref="Encrypt"/> so the agreement can be checked on its own
    /// against RFC 8291 Appendix A, which publishes the shared secret this must produce
    /// for the example's keys. That is the difference between "a message round-trips"
    /// and "a browser will accept it": a shared secret that differed from the standard
    /// one would still decrypt with a receiver built the same wrong way.
    /// </para>
    /// </summary>
    internal static (byte[] SharedSecret, byte[] SenderPublicKey) AgreeWithReceiver(
        byte[] receiverPublicKey,
        byte[]? ephemeralPrivateKey)
    {
        using var sender = CreateKeyAgreement(ephemeralPrivateKey);
        var senderPublicKey = ExportUncompressedPoint(sender.ExportParameters(false));

        using var receiver = ImportPublicPoint(receiverPublicKey);

        return (sender.DeriveRawSecretAgreement(receiver.PublicKey), senderPublicKey);
    }

    /// <summary>
    /// The two HKDF chains of RFC 8291 §3.4: the ECDH secret is first combined with the
    /// receiver's authentication secret (that is what makes the push service unable to
    /// forge a message), and the result is then expanded with the salt into the AES key
    /// and nonce of RFC 8188.
    /// </summary>
    internal static ContentKeys DeriveKeys(
        byte[] sharedSecret,
        byte[] authSecret,
        byte[] receiverPublicKey,
        byte[] senderPublicKey,
        byte[]? salt)
    {
        var keyInfo = new byte[KeyInfoPrefix.Length + receiverPublicKey.Length + senderPublicKey.Length];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        receiverPublicKey.CopyTo(keyInfo, KeyInfoPrefix.Length);
        senderPublicKey.CopyTo(keyInfo, KeyInfoPrefix.Length + receiverPublicKey.Length);

        var pseudorandomKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);
        var inputKeyingMaterial = HKDF.Expand(HashAlgorithmName.SHA256, pseudorandomKey, 32, keyInfo);

        var actualSalt = salt ?? RandomNumberGenerator.GetBytes(SaltLength);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, inputKeyingMaterial, actualSalt);

        return new ContentKeys(
            actualSalt,
            HKDF.Expand(HashAlgorithmName.SHA256, prk, KeyLength, ContentEncryptionKeyInfo),
            HKDF.Expand(HashAlgorithmName.SHA256, prk, NonceLength, NonceInfo));
    }

    /// <summary>
    /// RFC 8188's header: salt, record size, the key-id length and the key-id (here the
    /// sender's public point, as RFC 8291 §4 requires).
    /// </summary>
    private static byte[] BuildHeader(byte[] salt, byte[] senderPublicKey)
    {
        var header = new byte[SaltLength + 4 + 1 + senderPublicKey.Length];
        salt.CopyTo(header, 0);

        // Record size is a 32-bit big-endian integer, and it counts the plaintext, the
        // padding delimiter and the tag.
        var recordSize = (uint)RecordSize;
        header[SaltLength] = (byte)(recordSize >> 24);
        header[SaltLength + 1] = (byte)(recordSize >> 16);
        header[SaltLength + 2] = (byte)(recordSize >> 8);
        header[SaltLength + 3] = (byte)recordSize;

        header[SaltLength + 4] = (byte)senderPublicKey.Length;
        senderPublicKey.CopyTo(header, SaltLength + 5);

        return header;
    }

    private static byte[] DecodeReceiverKey(string p256dh)
    {
        if (!Base64Url.TryDecode(p256dh, VapidKeys.PublicKeyLength, out var point))
        {
            throw new ArgumentException(
                "The subscription's p256dh is not a base64url uncompressed P-256 point.", nameof(p256dh));
        }

        // The curve equation is checked here too. A subscription's key arrives from a browser
        // and is agreed a secret with; RFC 8291 §7's warning about unchecked points is about
        // exactly this step, and a point that is not on the curve has no agreed secret to have.
        if (!P256.IsOnCurve(point))
        {
            throw new ArgumentException(
                "The subscription's p256dh is not a point on P-256.", nameof(p256dh));
        }

        return point;
    }

    private static byte[] DecodeAuthSecret(string authSecret)
    {
        if (!Base64Url.TryDecode(authSecret, 16, out var secret))
        {
            throw new ArgumentException(
                "The subscription's auth secret is not 16 base64url octets.", nameof(authSecret));
        }

        return secret;
    }

    private static ECDiffieHellman CreateKeyAgreement(byte[]? privateKey)
    {
        if (privateKey is null)
        {
            return ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        }

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ecdh.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey
        });

        return ecdh;
    }

    private static ECDiffieHellman ImportPublicPoint(byte[] point)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        try
        {
            ecdh.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = point[1..(1 + CoordinateLength)],
                    Y = point[(1 + CoordinateLength)..]
                }
            });
        }
        catch (CryptographicException ex)
        {
            ecdh.Dispose();

            throw new ArgumentException(
                "The subscription's p256dh is not a valid point on P-256.", ex);
        }

        return ecdh;
    }

    private static byte[] ExportUncompressedPoint(ECParameters parameters)
    {
        var point = new byte[VapidKeys.PublicKeyLength];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 1 + CoordinateLength);
        return point;
    }

    /// <summary>What RFC 8188's key schedule produces for one message.</summary>
    internal readonly record struct ContentKeys(byte[] Salt, byte[] ContentEncryptionKey, byte[] Nonce);
}
