namespace FMS.Infrastructure.Notifications;

/// <summary>
/// base64url (RFC 4648 §5) with the padding optional on the way in.
///
/// Web Push speaks this dialect everywhere: a subscription's <c>p256dh</c> and
/// <c>auth</c>, and the VAPID keys themselves. Padding is tolerated when decoding
/// because browsers disagree about emitting it — Chrome omits it, and a subscription
/// pasted from a log or a support message may carry it — while encoding never writes
/// it, because RFC 8292's <c>k=</c> parameter is defined as unpadded.
/// </summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url. Throws <see cref="FormatException"/> on anything else.</summary>
    public static byte[] Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("The value is empty.");
        }

        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');

        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
            case 1: throw new FormatException("The value is not valid base64url.");
        }

        return Convert.FromBase64String(normalized);
    }

    /// <summary>True when the value decodes cleanly to exactly <paramref name="length"/> bytes.</summary>
    public static bool TryDecode(string? value, int length, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var decoded = Decode(value);
            if (decoded.Length != length)
            {
                return false;
            }

            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
