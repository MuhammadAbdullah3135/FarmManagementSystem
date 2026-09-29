using System.Security.Cryptography;
using FMS.Application.Notifications;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Infrastructure.Notifications;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Notifications;

/// <summary>
/// Registering and removing a device.
///
/// <para>
/// The values here are credentials, so the properties worth pinning are the unglamorous
/// ones: a malformed key is refused rather than stored (it would fail per alert, per
/// device, inside a scheduled job, with a phone that never buzzes as the only symptom), a
/// browser that re-subscribes does not accumulate rows, the same browser signing in as
/// somebody else moves rather than duplicates, and one person's device id can never reach
/// another person's row.
/// </para>
/// </summary>
public class PushSubscriptionServiceTests
{
    private const string ValidEndpoint = "https://fcm.googleapis.com/fcm/send/abc123";

    private sealed class FakePushSender : IPushSender
    {
        public bool IsConfigured { get; set; } = true;

        public Task<PushSendResult> SendAsync(
            PushSubscriptionTarget target,
            PushMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PushSendResult.Sent);
    }

    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// The service takes its "can this deployment push?" answer from the sender, so the key
    /// material is not this file's business — the sender is a fake, and the real one's key
    /// handling is covered by <c>VapidKeysTests</c>.
    /// </summary>
    private static PushSubscriptionService CreateService(
        FmsDbContext context,
        FakePushSender? sender = null,
        int maxSubscriptions = 5) =>
        new(context,
            sender ?? new FakePushSender(),
            Options.Create(new NotificationOptions()),
            Options.Create(new PushOptions
            {
                Enabled = true,
                Subject = "mailto:ops@example.com",
                // The public half is what a browser subscribes with, so the settings response
                // carries it; the private half is never read by this service.
                PublicKey = VapidKeys.Generate().PublicKeyBase64Url,
                MaxSubscriptionsPerUser = maxSubscriptions
            }),
            NullLogger<PushSubscriptionService>.Instance);

    /// <summary>A well-formed request, with the two key values a browser would send.</summary>
    private static RegisterPushSubscriptionRequest Request(
        string endpoint = ValidEndpoint,
        string? deviceLabel = "Chrome on Android")
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = receiver.ExportParameters(false).Q;

        var point = new byte[65];
        point[0] = 0x04;
        q.X!.CopyTo(point, 1);
        q.Y!.CopyTo(point, 33);

        return new RegisterPushSubscriptionRequest
        {
            Endpoint = endpoint,
            P256dh = Base64UrlEncode(point),
            Auth = Base64UrlEncode(RandomNumberGenerator.GetBytes(16)),
            DeviceLabel = deviceLabel
        };
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── Settings ────────────────────────────────────────────

    [Fact]
    public async Task GetSettings_ReportsTheDeploymentAndTheCallersOwnDevices()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        await service.RegisterAsync(farmId, userId, Request("https://push.example.net/one"));
        await service.RegisterAsync(farmId, userId, Request("https://push.example.net/two"));
        await service.RegisterAsync(farmId, someoneElse, Request("https://push.example.net/theirs"));

        var result = await service.GetSettingsAsync(farmId, userId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Enabled);

        // The public key is handed out only here: it is what the browser subscribes with.
        Assert.False(string.IsNullOrWhiteSpace(result.Value.VapidPublicKey));

        Assert.Equal(5, result.Value.MaxSubscriptionsPerUser);
        Assert.Equal(NotificationSeverity.Critical, result.Value.MinSeverityOnByDefault);

        // Two of the three rows, and never somebody else's.
        Assert.Equal(2, result.Value.Subscriptions.Count);
        Assert.DoesNotContain(result.Value.Subscriptions, s => !s.IsActive);
    }

    [Fact]
    public async Task GetSettings_WhenTheDeploymentCannotPush_SaysSoAndHandsOutNoKey()
    {
        using var context = CreateContext();
        var sender = new FakePushSender { IsConfigured = false };
        var service = CreateService(context, sender);

        var result = await service.GetSettingsAsync(Guid.NewGuid(), Guid.NewGuid());

        // Enabled is the server's answer, not the browser's: a deployment with no VAPID keys has
        // no push channel, and the screen must say so rather than prompt for a permission that
        // could never deliver anything.
        Assert.False(result.Value!.Enabled);
        Assert.Null(result.Value.VapidPublicKey);
    }

    // ── Registration ────────────────────────────────────────

    [Fact]
    public async Task Register_StoresTheDeviceAndListsItBack()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await service.RegisterAsync(farmId, userId, Request());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsActive);
        Assert.Equal("Chrome on Android", result.Value.DeviceLabel);

        var stored = await context.PushSubscriptions.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(ValidEndpoint, stored.Endpoint);
        // The keys are kept verbatim: they are what the payload is encrypted against.
        Assert.Equal(65, Base64UrlLength(stored.P256dh));
        Assert.Equal(16, Base64UrlLength(stored.Auth));

        // Bookkeeping, not business data: the audit log is for things a person did to the farm.
        Assert.IsAssignableFrom<IAuditLogExcluded>(stored);
    }

    [Fact]
    public async Task Register_WhenTheDeploymentCannotPush_IsRefusedAndStoresNothing()
    {
        using var context = CreateContext();
        var sender = new FakePushSender { IsConfigured = false };
        var service = CreateService(context, sender);

        var result = await service.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), Request());

        // Storing it would leave a device in the user's list looking healthy while every alert
        // went nowhere.
        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Empty(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Register_RefreshesTheSameEndpointInsteadOfAddingARow()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var first = await service.RegisterAsync(farmId, userId, Request());
        var second = await service.RegisterAsync(farmId, userId, Request());

        // A browser re-subscribes with new keys for the same endpoint; keeping the old ones
        // would encrypt for a key the browser no longer holds.
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Single(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Register_ClearsADisabledRowSoItCanReceiveAgain()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var userId = Guid.NewGuid();

        await service.RegisterAsync(Guid.NewGuid(), userId, Request());

        var stored = await context.PushSubscriptions.SingleAsync();
        stored.DisabledAtUtc = DateTime.UtcNow;
        stored.LastFailureReason = "the push service answered 410 Gone";
        stored.FailureCount = 3;
        await context.SaveChangesAsync();

        var result = await service.RegisterAsync(Guid.NewGuid(), userId, Request());

        // A browser that subscribes again is a working device again: the old verdict belongs to
        // the subscription that ended, not to this one.
        Assert.True(result.Value!.IsActive);
        Assert.Single(await context.PushSubscriptions.ToListAsync());

        var refreshed = await context.PushSubscriptions.AsNoTracking().SingleAsync();
        Assert.Null(refreshed.DisabledAtUtc);
        Assert.Null(refreshed.LastFailureReason);
        Assert.Equal(0, refreshed.FailureCount);
    }

    [Fact]
    public async Task Register_WithTheSameEndpointForAnotherUser_MovesTheRow()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await service.RegisterAsync(farmId, mine, Request());
        await service.RegisterAsync(farmId, theirs, Request());

        // One browser, one row. Without this, the person who signed in first would keep
        // receiving the other person's alerts on a device they no longer use — and the unique
        // index on the endpoint would refuse the second registration outright.
        var stored = Assert.Single(await context.PushSubscriptions.ToListAsync());
        Assert.Equal(theirs, stored.UserId);

        var mineNow = await service.GetSettingsAsync(farmId, mine);
        Assert.Empty(mineNow.Value!.Subscriptions);
    }

    [Fact]
    public async Task Register_RefusesMoreDevicesThanTheLimit()
    {
        using var context = CreateContext();
        var service = CreateService(context, maxSubscriptions: 2);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.RegisterAsync(farmId, userId, Request("https://push.example.net/one"));
        await service.RegisterAsync(farmId, userId, Request("https://push.example.net/two"));

        // A stuck client that re-subscribes on every visit must not grow the table without
        // bound, so the ceiling is a refusal rather than an eviction: nothing the user did not
        // ask for is removed.
        var third = await service.RegisterAsync(farmId, userId, Request("https://push.example.net/three"));

        Assert.False(third.IsSuccess);
        Assert.Equal("Validation", third.Error!.Code);
        Assert.Contains("2 devices", third.Error.Message);
        Assert.Equal(2, await context.PushSubscriptions.CountAsync());

        // ...but refreshing a device already on the list is not a new device, so it still works
        // at the ceiling — otherwise a user at the limit could never recover a stale key.
        var refresh = await service.RegisterAsync(farmId, userId, Request("https://push.example.net/one"));
        Assert.True(refresh.IsSuccess);
    }

    [Theory]
    [InlineData("", "endpoint")]
    [InlineData("http://push.example.net/insecure", "https")]
    [InlineData("not a url", "https")]
    public async Task Register_RejectsAnEndpointThatIsNotAnAbsoluteHttpsUrl(string endpoint, string expected)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), Request(endpoint));

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Contains(expected, result.Error.Message);
        Assert.Empty(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Register_RejectsKeysThatAreNotTheShapeTheSpecRequires()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var wrongKey = Request();
        wrongKey.P256dh = Base64UrlEncode(new byte[64]); // not an uncompressed point

        var wrongAuth = Request();
        wrongAuth.Auth = Base64UrlEncode(new byte[8]);   // not 16 octets

        var badKey = await service.RegisterAsync(farmId, userId, wrongKey);
        var badAuth = await service.RegisterAsync(farmId, userId, wrongAuth);

        Assert.Equal("Validation", badKey.Error!.Code);
        Assert.Contains("p256dh", badKey.Error.Message);
        Assert.Equal("Validation", badAuth.Error!.Code);
        Assert.Contains("auth", badAuth.Error.Message);
        Assert.Empty(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Register_KeepsALongDeviceLabelWithinTheColumn()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        await service.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), Request(deviceLabel: new string('x', 400)));

        var stored = await context.PushSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal(200, stored.DeviceLabel!.Length);
    }

    // ── Removal ─────────────────────────────────────────────

    [Fact]
    public async Task Unregister_RemovesTheCallersDevice()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var registered = await service.RegisterAsync(farmId, userId, Request());
        var result = await service.UnregisterAsync(farmId, userId, registered.Value!.Id);

        Assert.True(result.IsSuccess);
        // Deleted rather than disabled: this is the user's own act, not a push service reporting
        // a browser gone, and leaving it would show a device they just removed.
        Assert.Empty(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Unregister_OnAnotherUsersDevice_IsNotFoundAndChangesNothing()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var farmId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        var registered = await service.RegisterAsync(farmId, owner, Request());

        var result = await service.UnregisterAsync(farmId, someoneElse, registered.Value!.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
        Assert.Single(await context.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Unregister_StillWorksWhenPushIsTurnedOffDeploymentWide()
    {
        using var context = CreateContext();
        var farmId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Registered while it worked...
        var registered = await CreateService(context).RegisterAsync(farmId, userId, Request());

        // ...then the deployment loses its keys. Cleanup must not be blocked by that, or a
        // stale device could never be removed from the user's list.
        var disabledService = CreateService(context, new FakePushSender { IsConfigured = false });

        var result = await disabledService.UnregisterAsync(farmId, userId, registered.Value!.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(await context.PushSubscriptions.ToListAsync());
    }

    private static int Base64UrlLength(string value) =>
        Convert.FromBase64String(
            value.Replace('-', '+').Replace('_', '/')
                + new string('=', (4 - (value.Length % 4)) % 4)).Length;
}
