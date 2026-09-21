using System.Net;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The client used to hardcode the logged-in user's roles (`roles: []` on login,
/// `roles: ['SystemOwner']` on register) because <see cref="FMS.Application.Auth.AuthResponse"/>
/// never carried them. These tests pin the fix: the role claims that go into the
/// JWT are also returned on login, register, and refresh.
/// </summary>
public class AuthResponseRolesE2ETests : IClassFixture<AuthResponseRolesE2ETests.Factory>
{
    public class Factory : TestWebApplicationFactory
    {
        public const string Password = "TestPass123!";

        /// <summary>
        /// The shared seed user has a non-BCrypt placeholder hash and no roles, so
        /// give it a real password and the SystemOwner role before the tests run.
        /// </summary>
        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            await RoleSeeder.SeedRolesAsync(db);

            var user = await db.Users.FirstAsync(u => u.Id == JwtTokenHelper.TestUserId);
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);

            var systemOwner = await db.Roles.FirstAsync(r => r.Name == "SystemOwner");
            var alreadyLinked = await db.UserRoles
                .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == systemOwner.Id);
            if (!alreadyLinked)
            {
                db.UserRoles.Add(new UserRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = systemOwner.Id,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }
    }

    private readonly Factory _factory;

    public AuthResponseRolesE2ETests(Factory factory) => _factory = factory;

    private sealed class AuthResponseBody
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    [Fact]
    public async Task Login_ReturnsTheUsersRoles()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            email = "testuser@test.com",
            password = Factory.Password,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseBody>();
        Assert.NotNull(body);
        Assert.Contains("SystemOwner", body!.Roles);
    }

    [Fact]
    public async Task Refresh_KeepsRolesOnTheNewPair()
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "testuser@test.com",
            password = Factory.Password,
        });
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponseBody>();
        Assert.NotNull(loginBody);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = loginBody!.RefreshToken,
        });

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshBody = await refresh.Content.ReadFromJsonAsync<AuthResponseBody>();
        Assert.NotNull(refreshBody);
        Assert.Contains("SystemOwner", refreshBody!.Roles);
    }

    [Fact]
    public async Task Register_ReturnsTheInitialRole()
    {
        var email = $"roles-{Guid.NewGuid():N}@test.com";

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = Factory.Password,
            firstName = "Role",
            lastName = "Tester",
            accountName = "Role Test Account",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseBody>();
        Assert.NotNull(body);
        Assert.Contains("SystemOwner", body!.Roles);
    }
}
