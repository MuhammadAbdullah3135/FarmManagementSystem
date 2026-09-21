using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The notification centre's HTTP surface, exercised through the REAL
/// FarmContextMiddleware (not the permissive stub).
///
/// Two things are being proven here that a service-level test cannot:
/// <list type="bullet">
/// <item>read/dismiss state survives the round trip, so "mark read" is a real
/// change and not a client-side illusion;</item>
/// <item>the endpoints are farm-scoped like every other farm data route, so they
/// inherit subphase 3.1's farm-context enforcement without FarmContextMiddleware
/// needing a single new exemption.</item>
/// </list>
/// </summary>
public class NotificationApiE2ETests : IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>A second member of the seeded farm, so recipient isolation is testable.</summary>
        public static readonly Guid OtherMemberId = new("dddddddd-0000-0000-0000-000000000001");

        /// <summary>The unread notification belonging to the seeded test user.</summary>
        public Guid MyUnreadNotificationId { get; private set; }

        /// <summary>The unread notification belonging to the other member.</summary>
        public Guid TheirNotificationId { get; private set; }

        /// <summary>An already-read notification for the test user.</summary>
        public Guid MyReadNotificationId { get; private set; }

        /// <summary>A resolved notification for the test user (no longer actionable).</summary>
        public Guid MyResolvedNotificationId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            db.Users.Add(new User
            {
                Id = OtherMemberId,
                AccountId = seed.AccountId,
                Email = "other-member@notify.test",
                PasswordHash = "not-used-tests-authenticate-with-generated-tokens",
                FirstName = "Other",
                LastName = "Member",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.UserFarms.Add(new UserFarm
            {
                Id = Guid.NewGuid(),
                UserId = OtherMemberId,
                FarmId = seed.FarmId,
                // The read-only role: it can still read its own notifications,
                // because the notification centre is not a restricted module.
                Role = "Viewer",
                CreatedAt = DateTime.UtcNow
            });

            MyUnreadNotificationId = Guid.NewGuid();
            MyReadNotificationId = Guid.NewGuid();
            MyResolvedNotificationId = Guid.NewGuid();
            TheirNotificationId = Guid.NewGuid();

            db.Notifications.AddRange(
                NewNotification(MyUnreadNotificationId, seed.FarmId, JwtTokenHelper.TestUserId,
                    "Overdue: FMD", NotificationAlertTypes.OverdueVaccination, NotificationSeverity.Critical),
                NewNotification(MyReadNotificationId, seed.FarmId, JwtTokenHelper.TestUserId,
                    "Weight check due: LATE-001", NotificationAlertTypes.OverdueWeightCheck,
                    NotificationSeverity.Warning, readAtUtc: DateTime.UtcNow),
                NewNotification(MyResolvedNotificationId, seed.FarmId, JwtTokenHelper.TestUserId,
                    "Low stock: Hay Bales", NotificationAlertTypes.LowInventory,
                    NotificationSeverity.Warning, resolvedAtUtc: DateTime.UtcNow),
                NewNotification(TheirNotificationId, seed.FarmId, OtherMemberId,
                    "Overdue: FMD", NotificationAlertTypes.OverdueVaccination, NotificationSeverity.Critical));

            await db.SaveChangesAsync();
        }

        private static Notification NewNotification(
            Guid id,
            Guid farmId,
            Guid userId,
            string title,
            string alertType,
            string severity,
            DateTime? readAtUtc = null,
            DateTime? resolvedAtUtc = null) => new()
        {
            Id = id,
            FarmId = farmId,
            UserId = userId,
            AlertType = alertType,
            Severity = severity,
            SourceKey = $"{alertType}:{id}",
            Title = title,
            Message = "seeded for the notification API tests",
            Link = "/dashboard",
            CreatedAt = DateTime.UtcNow,
            ReadAtUtc = readAtUtc,
            ResolvedAtUtc = resolvedAtUtc
        };
    }

    // A factory per test rather than a shared fixture: these tests mutate read,
    // dismissed and preference state, and a shared in-memory store would make the
    // result depend on execution order.
    private readonly Factory _factory = new();
    private readonly ITestOutputHelper _output;

    public NotificationApiE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Reads through a fresh scope, then disposes it.</summary>
    private async Task<T> WithDbAsync<T>(Func<FmsDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await action(db);
    }

    /// <summary>
    /// The seeded identifiers, starting the host first. Touching <c>Seed</c> in a
    /// test's first line is what makes every other test independent of which one
    /// ran before it.
    /// </summary>
    private ApiSeedData.SeedIds Seed
    {
        get
        {
            _ = _factory.Host;
            return _factory.SeedData!;
        }
    }

    /// <summary>Authenticated client for <paramref name="userId"/> with an optional X-Farm-Id header.</summary>
    private HttpClient ClientFor(Guid userId, Guid? headerFarm = null)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(userId, new[] { "SystemOwner" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerFarm.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());
        }

        return client;
    }

    private HttpClient MyClient() => ClientFor(JwtTokenHelper.TestUserId, Seed.FarmId);

    private string Url(string suffix = "") => $"/api/farm/{Seed.FarmId}/notifications{suffix}";

    private async Task<NotificationListResponse> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Url(query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationListResponse>())!;
    }

    private sealed record NotificationResponse(
        Guid Id,
        string Title,
        bool IsRead,
        DateTime? ReadAtUtc,
        bool IsDismissed,
        bool IsResolved,
        DateTime? DeliveredAtUtc);

    private sealed record NotificationListResponse(
        List<NotificationResponse> Items,
        int TotalCount,
        int UnreadCount);

    // ── Required: marking read persists ─────────────────────

    [Fact]
    public async Task MarkRead_PersistsAndDropsTheUnreadCount()
    {
        var client = MyClient();

        var before = await ListAsync(client);
        Assert.Equal(1, before.UnreadCount);
        Assert.Equal(2, before.TotalCount); // the resolved one is hidden by default

        var response = await client.PostAsync(Url($"/{_factory.MyUnreadNotificationId}/read"), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var marked = (await response.Content.ReadFromJsonAsync<NotificationResponse>())!;
        Assert.True(marked.IsRead);
        Assert.NotNull(marked.ReadAtUtc);

        // The change is in the store, not just in the response: a freshly built
        // list view and a direct read of the row both agree.
        var after = await ListAsync(client);
        Assert.Equal(0, after.UnreadCount);
        var item = Assert.Single(after.Items, n => n.Id == _factory.MyUnreadNotificationId);
        Assert.True(item.IsRead);

        var stored = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Id == _factory.MyUnreadNotificationId));
        Assert.NotNull(stored.ReadAtUtc);

        _output.WriteLine($"unread {before.UnreadCount} -> {after.UnreadCount}");
    }

    [Fact]
    public async Task MarkAllRead_ClearsTheBadgeAndSurvivesARefetch()
    {
        var client = MyClient();

        var response = await client.PostAsync(Url("/read-all"), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ListAsync(client);
        Assert.Equal(0, after.UnreadCount);
        Assert.All(after.Items, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task Dismiss_HidesItFromTheDefaultListButKeepsTheRow()
    {
        var client = MyClient();

        var response = await client.PostAsync(Url($"/{_factory.MyUnreadNotificationId}/dismiss"), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dismissed = (await response.Content.ReadFromJsonAsync<NotificationResponse>())!;
        Assert.True(dismissed.IsDismissed);

        var defaultView = await ListAsync(client);
        Assert.DoesNotContain(defaultView.Items, n => n.Id == _factory.MyUnreadNotificationId);

        var withDismissed = await ListAsync(client, "?includeDismissed=true");
        Assert.Contains(withDismissed.Items, n => n.Id == _factory.MyUnreadNotificationId);

        // Historical, not deleted: the row is still there.
        var stored = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Id == _factory.MyUnreadNotificationId));
        Assert.NotNull(stored.DismissedAtUtc);
    }

    // ── Recipient isolation ─────────────────────────────────

    [Fact]
    public async Task Get_ReturnsOnlyTheCallersOwnNotifications()
    {
        var mine = await ListAsync(MyClient());
        var theirs = await ListAsync(ClientFor(Factory.OtherMemberId, Seed.FarmId));

        Assert.DoesNotContain(mine.Items, n => n.Id == _factory.TheirNotificationId);
        Assert.DoesNotContain(theirs.Items, n => n.Id == _factory.MyUnreadNotificationId);
        Assert.Single(theirs.Items);
        Assert.Equal(1, theirs.UnreadCount);
    }

    [Fact]
    public async Task AnotherMembersNotification_CannotBeReadOrDismissed()
    {
        var client = MyClient();

        var read = await client.PostAsync(Url($"/{_factory.TheirNotificationId}/read"), null);
        var dismiss = await client.PostAsync(Url($"/{_factory.TheirNotificationId}/dismiss"), null);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dismiss.StatusCode);

        // And nothing changed on the other member's row.
        var stored = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Id == _factory.TheirNotificationId));
        Assert.Null(stored.ReadAtUtc);
        Assert.Null(stored.DismissedAtUtc);
    }

    // ── Read-only role still owns its own notifications ─────

    [Fact]
    public async Task ViewerRole_CanReadItsOwnNotifications()
    {
        // The notification centre is not a role-restricted module: it holds the
        // caller's own data, and the dispatcher already withholds alerts a role
        // cannot act on. A Viewer must not be locked out of their own list.
        var client = ClientFor(Factory.OtherMemberId, Seed.FarmId);

        var response = await client.GetAsync(Url());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Preferences ─────────────────────────────────────────

    [Fact]
    public async Task Preferences_RoundTripThroughTheApi()
    {
        var client = MyClient();

        var initial = await client.GetFromJsonAsync<PreferenceSettingsResponse>(Url("/preferences"));
        Assert.NotNull(initial);
        Assert.Equal(NotificationAlertTypes.All.Count, initial!.Preferences.Count);
        // Nothing stored yet, so this is the Critical-threshold default.
        Assert.True(initial.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.OverdueVaccination).EmailEnabled);
        Assert.False(initial.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.LowInventory).EmailEnabled);

        var put = await client.PutAsJsonAsync(Url("/preferences"), new
        {
            preferences = new[]
            {
                // Both directions: turn email off for a type that defaults on, and
                // on for a type that defaults off.
                new { alertType = NotificationAlertTypes.OverdueVaccination, inAppEnabled = true, emailEnabled = false },
                new { alertType = NotificationAlertTypes.LowInventory, inAppEnabled = false, emailEnabled = true }
            }
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var updated = await client.GetFromJsonAsync<PreferenceSettingsResponse>(Url("/preferences"));
        Assert.False(updated!.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.OverdueVaccination).EmailEnabled);
        Assert.False(updated.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.LowInventory).InAppEnabled);
        Assert.True(updated.Preferences.Single(p =>
            p.AlertType == NotificationAlertTypes.LowInventory).EmailEnabled);

        // Persisted, not just echoed.
        var stored = await WithDbAsync(db => db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == JwtTokenHelper.TestUserId)
            .ToListAsync());
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Preferences_UnknownAlertType_IsRejected()
    {
        var client = MyClient();

        var response = await client.PutAsJsonAsync(Url("/preferences"), new
        {
            preferences = new[]
            {
                new { alertType = "NotARealAlertType", inAppEnabled = true, emailEnabled = true }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await WithDbAsync(db => db.NotificationPreferences.AsNoTracking().ToListAsync()));
    }

    private sealed record PreferenceResponse(string AlertType, bool InAppEnabled, bool EmailEnabled);

    private sealed record PreferenceSettingsResponse(List<PreferenceResponse> Preferences);

    // ── Farm-context protection carries over ────────────────

    [Fact]
    public async Task RouteFarm_DifferentFromTheHeaderFarm_IsRejected()
    {
        // A header-validated farm A with a route pointing at farm B is the exact
        // bypass subphase 3.1 closed. These endpoints must inherit it — which is
        // why they were made farm-scoped rather than account-scoped.
        var client = ClientFor(JwtTokenHelper.TestUserId, Seed.FarmId);

        var otherFarmId = Guid.NewGuid();
        var response = await client.GetAsync($"/api/farm/{otherFarmId}/notifications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_IsUnauthorized()
    {
        _ = _factory.Host;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Farm-Id", Seed.FarmId.ToString());

        var response = await client.GetAsync(Url());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
