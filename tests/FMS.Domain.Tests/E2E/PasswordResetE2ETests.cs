using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FMS.Application.Auth;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

public class PasswordResetE2ETests : IClassFixture<PasswordResetE2ETests.Factory>
{
    /// <summary>
    /// Captures the most recently generated raw reset token so tests
    /// can exercise the confirm-reset endpoint without needing to
    /// reverse-engineer the SHA-256 hash.
    /// </summary>
    public class TestEmailService : IEmailService
    {
        private readonly ILogger<TestEmailService> _logger;
        public string? LastToken { get; private set; }

        public TestEmailService(ILogger<TestEmailService> logger) => _logger = logger;

        public Task SendPasswordResetEmailAsync(string email, string resetLink)
        {
            // Extract raw token from the reset link URL
            var tokenStart = resetLink.IndexOf("token=", StringComparison.Ordinal);
            if (tokenStart >= 0)
            {
                LastToken = resetLink[(tokenStart + "token=".Length)..];
            }
            _logger.LogInformation("TestEmailService captured reset token for {Email}: {Token}", email, LastToken);
            return Task.CompletedTask;
        }
    }

    public class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        {
            base.ConfigureDbContextOptions(options);
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public PasswordResetE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private ApiSeedData.SeedIds Seed => _factory.SeedData ?? throw new InvalidOperationException("Host not initialized");

    private HttpClient GetClient()
    {
        _ = _factory.Host;
        return _factory.CreateClient();
    }

    private TestEmailService GetEmailService()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IEmailService>() as TestEmailService
            ?? throw new InvalidOperationException("TestEmailService not registered");
    }

    private async Task<string> RequestResetAndGetTokenAsync(HttpClient client, string email)
    {
        var emailService = GetEmailService();

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(emailService.LastToken);
        return emailService.LastToken!;
    }

    [Fact]
    public async Task ResetPassword_KnownEmail_ReturnsOk_NoTokenInBody()
    {
        var client = GetClient();
        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "testuser@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the response body does NOT contain a reset token
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("resetToken", body);
    }

    [Fact]
    public async Task ResetPassword_UnknownEmail_ReturnsOk_NoLeak()
    {
        var client = GetClient();
        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "nonexistent@example.com" });

        // Must return same 200 OK as a known email to prevent user enumeration
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmResetPassword_ValidToken_Succeeds()
    {
        var client = GetClient();
        var rawToken = await RequestResetAndGetTokenAsync(client, "testuser@test.com");

        // Confirm with new password
        var confirmResponse = await client.PostAsJsonAsync("/api/auth/confirm-reset-password", new
        {
            token = rawToken,
            newPassword = "NewSecure123!"
        });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        // Login with the new password should succeed
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "testuser@test.com",
            password = "NewSecure123!"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.NotNull(loginBody);
        Assert.NotEmpty(loginBody!.AccessToken);
    }

    [Fact]
    public async Task ConfirmResetPassword_ExpiredToken_ReturnsBadRequest()
    {
        var client = GetClient();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FmsDbContext>();

        var user = await dbContext.Users.FirstAsync(u => u.Email == "testuser@test.com");

        // Insert a token that expired 1 hour ago
        var expiredToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("expired-token-value"))).ToLowerInvariant(),
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        dbContext.PasswordResetTokens.Add(expiredToken);
        await dbContext.SaveChangesAsync();

        // Confirm with the expired token
        var response = await client.PostAsJsonAsync("/api/auth/confirm-reset-password", new
        {
            token = "expired-token-value",
            newPassword = "NewSecure123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("expired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmResetPassword_UsedToken_ReturnsBadRequest()
    {
        var client = GetClient();
        var rawToken = await RequestResetAndGetTokenAsync(client, "testuser@test.com");

        // First use — should succeed
        var firstResponse = await client.PostAsJsonAsync("/api/auth/confirm-reset-password", new
        {
            token = rawToken,
            newPassword = "UsedToken123!"
        });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Second use — should fail (token already used)
        var secondResponse = await client.PostAsJsonAsync("/api/auth/confirm-reset-password", new
        {
            token = rawToken,
            newPassword = "AnotherPass123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);

        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("already been used", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmResetPassword_InvalidToken_ReturnsBadRequest()
    {
        var client = GetClient();
        var response = await client.PostAsJsonAsync("/api/auth/confirm-reset-password", new
        {
            token = "completely-fake-token-that-will-not-match",
            newPassword = "SomePassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid or expired", body, StringComparison.OrdinalIgnoreCase);
    }

    // DTO for deserialization
    private class LoginResponseBody
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
}
