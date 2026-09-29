using FMS.API.Security;
using FMS.Application.Notifications;
using FMS.Infrastructure.Notifications;

namespace FMS.Domain.Tests.Notifications;

/// <summary>
/// VAPID key handling, and what the host does with a configuration that cannot deliver.
///
/// <para>
/// The failure being prevented is the quiet one: with push enabled and unusable keys, every
/// push is refused by the push service, nothing in the product looks broken, and the only
/// symptom is a phone that never buzzes. So an unusable configuration outside Development
/// is a startup failure, and inside Development the channel is turned off and said out loud.
/// </para>
/// </summary>
public class PushConfigurationGuardTests
{
    private static PushOptions Configured(Action<PushOptions>? tweak = null)
    {
        var keys = VapidKeys.Generate();

        var options = new PushOptions
        {
            Enabled = true,
            Subject = "mailto:ops@example.com",
            PublicKey = keys.PublicKeyBase64Url,
            PrivateKey = ToBase64Url(keys.PrivateKey)
        };

        tweak?.Invoke(options);
        return options;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── The key pair ────────────────────────────────────────

    [Fact]
    public void TryParse_AcceptsAGeneratedPair()
    {
        var keys = VapidKeys.Generate();

        Assert.True(VapidKeys.TryParse(
            keys.PublicKeyBase64Url, ToBase64Url(keys.PrivateKey), out var parsed, out var problem));

        Assert.Null(problem);
        Assert.Equal(65, parsed!.PublicKey.Length);

        // The public half is the uncompressed point of the private half, which is what makes the
        // JWT it signs verifiable by the browser that subscribed with it.
        Assert.Equal(keys.PublicKeyBase64Url, parsed.PublicKeyBase64Url);
    }

    [Fact]
    public void TryParse_DerivesThePublicHalfWhenOnlyTheSecretIsConfigured()
    {
        var keys = VapidKeys.Generate();

        Assert.True(VapidKeys.TryParse(null, ToBase64Url(keys.PrivateKey), out var parsed, out _));

        // A deployment that supplies only the private key still needs something to hand a
        // browser, and deriving it is what the scalar knows how to do.
        Assert.Equal(keys.PublicKeyBase64Url, parsed!.PublicKeyBase64Url);
    }

    [Fact]
    public void TryParse_RefusesAHalvesThatDoNotBelongTogether()
    {
        var one = VapidKeys.Generate();
        var another = VapidKeys.Generate();

        // Both halves are individually valid, and the pair is not: a deployment that pasted a
        // public key from one and a private key from another would sign JWTs the browser cannot
        // verify, and find out from silence.
        Assert.False(VapidKeys.TryParse(
            one.PublicKeyBase64Url, ToBase64Url(another.PrivateKey), out _, out var problem));

        Assert.Contains("does not belong", problem);
    }

    [Theory]
    [InlineData("not base64url!", "not a base64url P-256 private scalar")]
    [InlineData("", "not a base64url P-256 private scalar")]
    public void TryParse_RefusesSomethingThatIsNotAScalar(string privateKey, string expected)
    {
        Assert.False(VapidKeys.TryParse(null, privateKey, out _, out var problem));
        Assert.Contains(expected, problem);
    }

    [Fact]
    public void TryParse_RefusesAPublicPointThatIsNotOnTheCurve()
    {
        var keys = VapidKeys.Generate();
        var tampered = keys.PublicKeyBase64Url.ToCharArray();
        tampered[10] = tampered[10] == 'A' ? 'B' : 'A';

        // RFC 8291 §7 requires the check: an unchecked point can leak the private key.
        var accepted = VapidKeys.TryParse(
            new string(tampered), ToBase64Url(keys.PrivateKey), out _, out var problem);

        Assert.False(accepted);
        Assert.NotNull(problem);
    }

    [Fact]
    public void BuildAuthorizationHeader_SignsForTheEndpointsOriginOnly()
    {
        var keys = VapidKeys.Generate();
        Assert.True(VapidKeys.TryParse(keys.PublicKeyBase64Url, ToBase64Url(keys.PrivateKey), out var parsed, out _));

        var header = parsed!.BuildAuthorizationHeader(
            "https://fcm.googleapis.com/fcm/send/abc123", "mailto:ops@example.com");

        Assert.NotNull(header);
        Assert.StartsWith("vapid t=", header);

        // The audience is the push service's origin, per RFC 8292, and the key rides along so
        // the service can verify the signature without a round trip.
        var claims = DecodeClaims(header!);
        Assert.Contains("\"aud\":\"https://fcm.googleapis.com\"", claims);
        Assert.Contains("\"sub\":\"mailto:ops@example.com\"", claims);
        Assert.Contains($"k={keys.PublicKeyBase64Url}", header);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/send/abc")]   // not https
    [InlineData("not a url")]
    [InlineData("")]
    public void BuildAuthorizationHeader_RefusesAnEndpointItCannotVouchFor(string endpoint)
    {
        Assert.Null(VapidKeys.Generate().BuildAuthorizationHeader(endpoint, "mailto:ops@example.com"));
    }

    private static string DecodeClaims(string header)
    {
        var token = header["vapid t=".Length..header.IndexOf(",k=", StringComparison.Ordinal)];
        var claims = token.Split('.')[1];

        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            claims.Replace('-', '+').Replace('_', '/')
                + new string('=', (4 - (claims.Length % 4)) % 4)));
    }

    // ── Startup ─────────────────────────────────────────────

    [Fact]
    public void EnsureUsable_WithAUsableConfiguration_SaysNothing()
    {
        Assert.Null(PushConfigurationGuard.EnsureUsable(Configured(), "Production", isDevelopment: false));
    }

    [Fact]
    public void EnsureUsable_WithPushOff_SaysNothingInDevelopment()
    {
        // The default deployment shape: no keys, no push, nothing to report.
        Assert.Null(PushConfigurationGuard.EnsureUsable(
            new PushOptions(), "Development", isDevelopment: true));
    }

    [Fact]
    public void EnsureUsable_WithPushOff_SaysSoOutsideDevelopment()
    {
        // Not a failure — a farm that expected alerts on a phone should hear it once rather
        // than find out at the wrong moment.
        var warning = PushConfigurationGuard.EnsureUsable(
            new PushOptions(), "Production", isDevelopment: false);

        Assert.NotNull(warning);
        Assert.Contains("turned off", warning);
    }

    [Theory]
    [InlineData("", "Push:Subject is not configured.")]
    [InlineData("ops@example.com", "must start with 'mailto:' or 'https://'")]
    public void EnsureUsable_WithAnUnusableConfiguration_RefusesToStartOutsideDevelopment(
        string subject, string expected)
    {
        var options = Configured(o => o.Subject = subject);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => PushConfigurationGuard.EnsureUsable(options, "Production", isDevelopment: false));

        Assert.Contains(expected, thrown.Message);
        Assert.Contains("Refusing to start", thrown.Message);
    }

    [Fact]
    public void EnsureUsable_WithMismatchedKeys_RefusesToStartOutsideDevelopment()
    {
        var options = Configured(o => o.PublicKey = VapidKeys.Generate().PublicKeyBase64Url);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => PushConfigurationGuard.EnsureUsable(options, "Production", isDevelopment: false));

        Assert.Contains("does not belong", thrown.Message);
    }

    [Fact]
    public void EnsureUsable_WithUnusableKeysInDevelopment_TurnsPushOffAndSaysWhy()
    {
        var options = Configured(o => o.PrivateKey = string.Empty);

        var warning = PushConfigurationGuard.EnsureUsable(options, "Development", isDevelopment: true);

        Assert.NotNull(warning);
        Assert.False(options.Enabled);
        Assert.Contains("push off in Development", warning);
    }

    [Fact]
    public void Validate_AcceptsTheKeysAndSubjectARealDeploymentWouldUse()
    {
        Assert.Null(PushConfigurationGuard.Validate(Configured()));

        // An https: subject is equally valid, and is what RFC 8292 suggests for a service
        // rather than a person.
        Assert.Null(PushConfigurationGuard.Validate(Configured(o => o.Subject = "https://fms.example.com")));
    }
}
