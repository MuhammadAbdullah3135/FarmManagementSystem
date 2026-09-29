using System.Security.Cryptography;
using System.Text;
using FMS.Infrastructure.Notifications;

namespace FMS.Domain.Tests.Notifications;

/// <summary>
/// Web Push encryption, checked against the standard rather than against itself.
///
/// <para>
/// The first test is the load-bearing one. RFC 8291 §5 publishes a complete message —
/// keys, salt, plaintext and the exact 145-octet body a push service would receive —
/// and Appendix A publishes every intermediate value along the way. Encrypting the
/// RFC's plaintext with the RFC's keys and getting that body back proves the whole
/// chain at once: the ECDH agreement, the authentication-secret mixing, both HKDF
/// expansions, the header layout and the AES-GCM record. A single wrong octet in any
/// of them changes the ciphertext.
/// </para>
///
/// <para>
/// That matters more here than in most places, because the only other thing that can
/// accept this output is a real browser: without the vector, "the encryption works"
/// would mean "it round-trips through our own decryption", which a symmetric mistake
/// (a nonce derived differently on both sides, a header both sides agree is wrong)
/// would happily pass. So the second test does round-trip — it is what proves a
/// *generated* message is well-formed and that two sends never reuse a salt — but it
/// is the RFC's example, not it, that pins correctness.
/// </para>
/// </summary>
public class WebPushEncryptionTests
{
    // ── RFC 8291 §5 and Appendix A ───────────────────────────

    private const string RfcPlaintext = "When I grow up, I want to be a watermelon";

    private const string RfcAuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";

    private const string RfcReceiverPublicKey =
        "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";

    private const string RfcSenderPrivateKey = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";

    private const string RfcSalt = "DGv6ra1nlYgDCS1FRnbzlw";

    /// <summary>
    /// The body from §5, with its line wrapping removed (and the one space Appendix A
    /// left inside it, which only exists to help the reader see the octet boundaries).
    /// </summary>
    private const string RfcBody =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIg"
        + "Dll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVB"
        + "St2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact]
    public void Encrypt_ReproducesTheRfc8291ExampleByteForByte()
    {
        var body = WebPushEncryption.Encrypt(
            RfcReceiverPublicKey,
            RfcAuthSecret,
            Encoding.ASCII.GetBytes(RfcPlaintext),
            FromBase64Url(RfcSalt),
            FromBase64Url(RfcSenderPrivateKey));

        // Compared as octets, not as text: the encoding is a presentation detail, and a
        // byte-level diff names the octet that is wrong instead of the base64 character.
        AssertSameOctets(FromBase64Url(RfcBody), body);
    }

    /// <summary>
    /// Equality with the offset of the first differing octet in the failure message.
    ///
    /// Worth the helper because this message is 144 octets long and the useful question
    /// is always the same one — the header, the ciphertext or the tag — and xUnit's own
    /// diff elides exactly that.
    /// </summary>
    private static void AssertSameOctets(byte[] expected, byte[] actual)
    {
        if (expected.Length == actual.Length)
        {
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    Assert.Fail(
                        $"First difference at octet {index} of {expected.Length} "
                        + $"(header is 0..86, ciphertext 86..128, tag 128..144): "
                        + $"expected {Convert.ToHexString(expected[index..])}, "
                        + $"got {Convert.ToHexString(actual[index..])}.");
                }
            }

            return;
        }

        Assert.Fail($"Lengths differ: expected {expected.Length} octets, got {actual.Length}.");
    }

    /// <summary>
    /// Appendix A's header: 16 octets of salt, <c>rs</c> = 4096, a key-id length of 65,
    /// and the sender's public point. Asserted separately from the body above because it
    /// is the part a push service itself may look at, and because a header that drifted
    /// would be a *silently* different message rather than a failed decryption.
    /// </summary>
    [Fact]
    public void Encrypt_WritesTheRfc8188HeaderTheExampleShows()
    {
        var body = WebPushEncryption.Encrypt(
            RfcReceiverPublicKey,
            RfcAuthSecret,
            Encoding.ASCII.GetBytes(RfcPlaintext),
            FromBase64Url(RfcSalt),
            FromBase64Url(RfcSenderPrivateKey));

        Assert.Equal(RfcSalt, Base64UrlForAssertions(body[..16]));

        // rs: 4096 as a big-endian 32-bit integer.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x10, 0x00 }, body[16..20]);

        Assert.Equal(65, body[20]);
        Assert.Equal(
            "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8",
            Base64UrlForAssertions(body[21..86]));

        // header(86) + plaintext(41 + delimiter) + tag(16). The RFC's Content-Length of
        // 145 for this message disagrees with its own base64 by one octet; the body it
        // publishes decodes to 144, which is what this asserts.
        Assert.Equal(144, body.Length);
    }

    /// <summary>
    /// The key schedule, step by step against RFC 8291 Appendix A.
    ///
    /// <para>
    /// This is deliberately finer-grained than the whole-message test above it. When a
    /// message is wrong, the useful question is <em>which</em> step is wrong: the
    /// agreement, the authentication-secret mixing, or the content key and nonce
    /// expansion. Appendix A publishes all three, so the answer here is one assertion
    /// wide, and a change to any of them fails with the step named instead of with
    /// "the ciphertext differs".
    /// </para>
    /// </summary>
    [Fact]
    public void DeriveKeys_ProducesTheIntermediateValuesFromAppendixA()
    {
        var (sharedSecret, senderPublicKey) = WebPushEncryption.AgreeWithReceiver(
            FromBase64Url(RfcReceiverPublicKey), FromBase64Url(RfcSenderPrivateKey));

        // ecdh_secret, and the sender point the header must carry.
        Assert.Equal(
            "kyrL1jIIOHEzg3sM2ZWRHDRB62YACZhhSlknJ672kSs",
            Base64UrlForAssertions(sharedSecret));
        Assert.Equal(
            "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8",
            Base64UrlForAssertions(senderPublicKey));

        var keys = WebPushEncryption.DeriveKeys(
            sharedSecret,
            FromBase64Url(RfcAuthSecret),
            FromBase64Url(RfcReceiverPublicKey),
            senderPublicKey,
            FromBase64Url(RfcSalt));

        // CEK and NONCE: the two values RFC 8291 §3.4 derives from IKM and the salt.
        Assert.Equal("oIhVW04MRdy2XN9CiKLxTg", Base64UrlForAssertions(keys.ContentEncryptionKey));
        Assert.Equal("4h_95klXJ5E_qnoN", Base64UrlForAssertions(keys.Nonce));
        Assert.Equal(RfcSalt, Base64UrlForAssertions(keys.Salt));
    }

    // ── A generated message ─────────────────────────────────

    [Fact]
    public void Encrypt_RoundTripsThroughAReceiverThatHoldsThePrivateKey()
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverPublicKey = UncompressedPoint(receiver);
        var authSecret = RandomNumberGenerator.GetBytes(16);

        var payload = Encoding.UTF8.GetBytes("{\"title\":\"Overdue vaccination\",\"count\":3}");

        var body = WebPushEncryption.Encrypt(
            Base64Url(receiverPublicKey),
            Base64Url(authSecret),
            payload);

        var decrypted = DecryptLikeAReceiver(body, receiver, receiverPublicKey, authSecret);

        Assert.Equal(payload, decrypted);
    }

    /// <summary>
    /// Two sends must never share a salt.
    ///
    /// <para>
    /// The salt and the ephemeral key are the only things standing between two
    /// ciphertexts and a nonce reuse, which is the one way to break AES-GCM. A constant
    /// salt would still round-trip through the receiver above, so this is the assertion
    /// that catches it.
    /// </para>
    /// </summary>
    [Fact]
    public void Encrypt_GeneratesAFreshSaltForEveryMessage()
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverPublicKey = UncompressedPoint(receiver);

        var first = WebPushEncryption.Encrypt(
            Base64Url(receiverPublicKey), Base64Url(RandomNumberGenerator.GetBytes(16)), "one"u8);
        var second = WebPushEncryption.Encrypt(
            Base64Url(receiverPublicKey), Base64Url(RandomNumberGenerator.GetBytes(16)), "one"u8);

        Assert.NotEqual(first[..16], second[..16]);

        // ...and a fresh ephemeral key, which is the sender's public point in the header.
        Assert.NotEqual(first[21..86], second[21..86]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Encrypt_RefusesAPayloadThatWouldNotFitOneRecord()
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = Base64Url(RandomNumberGenerator.GetBytes(16));
        var p256dh = Base64Url(UncompressedPoint(receiver));

        // One octet more than a single record can hold: RFC 8291 §4 forbids splitting
        // a push message across records, so this must fail loudly on our side rather
        // than be truncated into a message the browser cannot read.
        var tooLarge = new byte[WebPushEncryption.MaxPayloadLength + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebPushEncryption.Encrypt(p256dh, auth, tooLarge));

        // The largest payload that does fit is accepted.
        var largest = new byte[WebPushEncryption.MaxPayloadLength];
        var body = WebPushEncryption.Encrypt(p256dh, auth, largest);

        // The header (86 octets: salt, rs, key-id length and a 65-octet key), the
        // plaintext with its delimiter, and the 16-octet tag. The record size in the
        // header covers only the latter two, which is why the largest payload is
        // 4096 − 86 − 1 − 16 = 3993 and not 4010.
        Assert.Equal(86 + largest.Length + 1 + 16, body.Length);
    }

    [Theory]
    [InlineData("not-base64url!")]
    [InlineData("AAAA")]                              // wrong length
    [InlineData("AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Encrypt_RefusesAReceiverKeyThatIsNotAP256Point(string p256dh)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            WebPushEncryption.Encrypt(p256dh, Base64Url(RandomNumberGenerator.GetBytes(16)), "hello"u8));
    }

    [Fact]
    public void Encrypt_RefusesAnAuthSecretThatIsNot16Octets()
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() =>
            WebPushEncryption.Encrypt(Base64Url(UncompressedPoint(receiver)), "c2hvcnQ", "hello"u8));
    }

    // ── Receiver-side helpers ───────────────────────────────
    //
    // Deliberately a second, independent implementation of the *decryption* half: the
    // point is to derive the keys again from the wire format alone, exactly as a browser
    // does, rather than to call anything the sender uses.

    private static byte[] DecryptLikeAReceiver(
        byte[] body,
        ECDiffieHellman receiver,
        byte[] receiverPublicKey,
        byte[] authSecret)
    {
        var salt = body[..16];
        var senderPublicKey = body[21..86];
        var record = body[86..];

        if (body[20] != 65)
        {
            throw new InvalidOperationException($"Unexpected key-id length {body[20]}.");
        }

        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        sender.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = senderPublicKey[1..33],
                Y = senderPublicKey[33..]
            }
        });

        var sharedSecret = receiver.DeriveRawSecretAgreement(sender.PublicKey);

        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), receiverPublicKey, senderPublicKey);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);
        var inputKeyingMaterial = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, inputKeyingMaterial, salt);
        var cek = HKDF.Expand(
            HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(
            HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var ciphertext = record[..^16];
        var tag = record[^16..];
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(cek, 16))
        {
            // RFC 8188 §2: the AEAD is invoked with no associated data. A receiver that
            // passed the header here would accept our messages and reject a browser's.
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        // RFC 8291 §4: the receiver must check the delimiter and is free to discard
        // anything that does not end in it.
        Assert.Equal(0x02, plaintext[^1]);

        return plaintext[..^1];
    }

    private static byte[] Concat(byte[] first, byte[] second, byte[] third)
    {
        var result = new byte[first.Length + second.Length + third.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        third.CopyTo(result, first.Length + second.Length);
        return result;
    }

    private static byte[] UncompressedPoint(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        var point = new byte[65];
        point[0] = 0x04;
        q.X!.CopyTo(point, 1);
        q.Y!.CopyTo(point, 33);
        return point;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// The RFC publishes in base64url, so the test decodes it as such. Written out
    /// here rather than shared with the implementation, so the vector test does not
    /// depend on the helper it is checking.
    /// </summary>
    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized + new string('=', (4 - (normalized.Length % 4)) % 4));
    }

    /// <summary>
    /// The same encoding the wire format uses, for comparing against the RFC's text.
    /// <c>Convert.ToBase64String</c> would be a different alphabet.
    /// </summary>
    private static string Base64UrlForAssertions(byte[] bytes) => Base64Url(bytes);

    private static string Base64UrlForAssertions(ReadOnlySpan<byte> bytes) => Base64Url(bytes.ToArray());
}
