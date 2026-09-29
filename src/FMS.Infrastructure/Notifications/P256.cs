using System.Numerics;

namespace FMS.Infrastructure.Notifications;

/// <summary>
/// The little bit of P-256 arithmetic this feature needs: converting between the
/// uncompressed point form used on the wire and a pair of coordinates, and checking that a
/// point is actually on the curve.
///
/// <para>
/// The on-curve check is not decoration. RFC 8291 §7 requires it of both peers — "failure
/// to validate a public key can allow an attacker to extract a private key" — and it cannot
/// be delegated to <see cref="System.Security.Cryptography.ECDiffieHellman"/>: importing a
/// point with a fabricated X coordinate and a copied Y coordinate succeeds, because the
/// runtime only bounds the coordinates, not the curve equation. A test in
/// <c>PushConfigurationGuardTests</c> is what proved that, by mutating one character of a
/// valid key and watching it through.
/// </para>
///
/// <para>
/// The parameters are the standard NIST P-256 ones (the curve behind every Web Push
/// subscription), spelled out here rather than derived from the platform, so this check and
/// the wire format cannot drift apart.
/// </para>
/// </summary>
internal static class P256
{
    /// <summary>Bytes of one coordinate: an uncompressed point is <c>0x04 || X || Y</c>.</summary>
    public const int CoordinateLength = 32;

    /// <summary>Bytes of an uncompressed point.</summary>
    public const int PointLength = 1 + (2 * CoordinateLength);

    /// <summary>
    /// The field prime, with a leading zero so BigInteger parses it as unsigned rather than
    /// reading the top bit as a sign.
    /// </summary>
    private static readonly BigInteger Prime = BigInteger.Parse(
        "0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF",
        System.Globalization.NumberStyles.HexNumber);

    /// <summary>The curve's <c>b</c> parameter.</summary>
    private static readonly BigInteger B = BigInteger.Parse(
        "05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B",
        System.Globalization.NumberStyles.HexNumber);

    /// <summary>The curve's <c>a</c> parameter, which for P-256 is −3.</summary>
    private static readonly BigInteger A = Prime - 3;

    /// <summary>True when the point is the solution to <c>y² = x³ + ax + b (mod p)</c>.</summary>
    public static bool IsOnCurve(ReadOnlySpan<byte> point)
    {
        if (point.Length != PointLength || point[0] != 0x04)
        {
            return false;
        }

        var x = new BigInteger(point[1..(1 + CoordinateLength)], isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(point[(1 + CoordinateLength)..], isUnsigned: true, isBigEndian: true);

        // A coordinate at or beyond the field prime is not a field element at all. Worth
        // stating separately: the equation below would compare two reduced values and accept
        // a coordinate that only coincides with a valid one modulo p.
        if (x >= Prime || y >= Prime)
        {
            return false;
        }

        var left = BigInteger.ModPow(y, 2, Prime);
        var right = BigInteger.Remainder(
            BigInteger.ModPow(x, 3, Prime) + (A * x) + B, Prime);

        return left == right;
    }

    /// <summary>Encodes two coordinates as the uncompressed point the wire format uses.</summary>
    public static byte[] Encode(byte[] x, byte[] y)
    {
        var point = new byte[PointLength];
        point[0] = 0x04;
        x.CopyTo(point, 1);
        y.CopyTo(point, 1 + CoordinateLength);
        return point;
    }
}
