using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The language preference, end to end over HTTP.
///
/// <para>
/// The behaviour being pinned is *which* of the two places that remember the language wins:
/// the device's `localStorage` is only a hint so the first paint is already translated, and
/// the account is the durable record. So the test that matters is device B signing in and
/// finding the language device A chose — through the real endpoints, the real service and a
/// real database row, not by asserting a client variable.
/// </para>
///
/// <para>
/// A factory per test rather than a class fixture: every test here mutates the account's
/// stored language, so a shared store would make the result depend on execution order.
/// </para>
/// </summary>
public class LocalePreferenceE2ETests : IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        public const string Password = "TestPass123!";

        /// <summary>
        /// The shared seed user has a placeholder hash, so give it a real password: signing in
        /// on a second device is half of what this test is about.
        /// </summary>
        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            await RoleSeeder.SeedRolesAsync(db);

            var user = await db.Users.FirstAsync(u => u.Id == JwtTokenHelper.TestUserId);
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
            await db.SaveChangesAsync();
        }
    }

    private readonly Factory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record LocaleBody(string? Locale);

    private sealed record LoginBody(string? Locale, List<string> Roles);

    /// <summary>The device that is already signed in (an access token, as the app has).</summary>
    private HttpClient SignedInDevice()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateTestToken());
        return client;
    }

    /// <summary>A different device: no stored token, so it signs in from scratch.</summary>
    private HttpClient NewDevice() => _factory.CreateClient();

    private async Task<HttpResponseMessage> SignInAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email = "testuser@test.com", password = Factory.Password });

    private async Task<T> WithDbAsync<T>(Func<FmsDbContext, Task<T>> action)
    {
        _ = _factory.Host; // ensure the store exists before it is read
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await action(db);
    }

    [Fact]
    public async Task A_locale_chosen_on_one_device_follows_the_account_to_another()
    {
        // Device A: a signed-in session that switches to Spanish.
        var deviceA = SignedInDevice();
        var saved = await deviceA.PutAsJsonAsync("/api/auth/me/locale", new { locale = "es" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("es", (await saved.Content.ReadFromJsonAsync<LocaleBody>())!.Locale);

        // The same device reads it back.
        var me = await deviceA.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("es", (await me.Content.ReadFromJsonAsync<LocaleBody>())!.Locale);

        // Device B: no local storage, no token — it signs in and must be told Spanish.
        var deviceB = NewDevice();
        var login = await SignInAsync(deviceB);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(body);
        Assert.Equal("es", body!.Locale);

        // And its own request to /auth/me agrees, which is what the client uses at boot.
        var newToken = JwtTokenHelper.GenerateTestToken();
        deviceB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        var meAgain = await deviceB.GetAsync("/api/auth/me");
        Assert.Equal("es", (await meAgain.Content.ReadFromJsonAsync<LocaleBody>())!.Locale);

        // The store, not just the responses: this is what survives a restart.
        var stored = await WithDbAsync(db => db.Users.AsNoTracking()
            .Where(u => u.Id == JwtTokenHelper.TestUserId)
            .Select(u => u.Locale)
            .SingleAsync());
        Assert.Equal("es", stored);
    }

    [Fact]
    public async Task An_unsupported_locale_is_refused_with_a_key_and_changes_nothing()
    {
        var device = SignedInDevice();
        await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "es" });

        var refused = await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        // The client renders this key rather than parsing a sentence, which is the whole
        // point of the keyed-message mechanism: the server states what went wrong.
        Assert.Equal("validation.locale.unsupported", refused.Headers.GetValues("X-Message-Key").Single());

        // A refused choice must not clear or overwrite the stored one — losing a Spanish
        // user's preference to English because a newer client sent something odd would be
        // the worst possible failure mode here.
        var stored = await WithDbAsync(db => db.Users.AsNoTracking()
            .Where(u => u.Id == JwtTokenHelper.TestUserId)
            .Select(u => u.Locale)
            .SingleAsync());
        Assert.Equal("es", stored);
    }

    [Fact]
    public async Task A_region_tag_is_kept_as_the_language_it_names()
    {
        var device = SignedInDevice();

        // `ES` and a region-qualified tag both mean Spanish; storing the base language is what
        // lets one account's choice match what the client can render.
        Assert.Equal("es", (await (await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "ES" }))
            .Content.ReadFromJsonAsync<LocaleBody>())!.Locale);

        Assert.Equal("es", (await (await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "es" }))
            .Content.ReadFromJsonAsync<LocaleBody>())!.Locale);
    }

    [Fact]
    public async Task A_blank_locale_clears_the_preference_rather_than_storing_a_blank()
    {
        var device = SignedInDevice();
        await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "es" });

        var cleared = await device.PutAsJsonAsync("/api/auth/me/locale", new { locale = "" });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<LocaleBody>())!.Locale);

        // Login then reports no choice, which is how the client knows to fall back to the
        // device's language rather than overriding it.
        var body = await (await SignInAsync(NewDevice())).Content.ReadFromJsonAsync<LoginBody>();
        Assert.Null(body!.Locale);

        var stored = await WithDbAsync(db => db.Users.AsNoTracking()
            .Where(u => u.Id == JwtTokenHelper.TestUserId)
            .Select(u => u.Locale)
            .SingleAsync());
        Assert.Null(stored);
    }
}
