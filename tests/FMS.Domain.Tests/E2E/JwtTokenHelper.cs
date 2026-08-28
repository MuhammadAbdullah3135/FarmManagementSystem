using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Generates a minimal valid JWT for test purposes.
/// Uses a fixed user ID so the FarmContextMiddleware can find the UserFarm entry.
/// </summary>
public static class JwtTokenHelper
{
    /// <summary>
    /// The fixed user ID used in test JWT tokens. Must match the User seeded in ApiSeedData.
    /// </summary>
    public static readonly Guid TestUserId = new("11111111-1111-1111-1111-111111111111");

    public static string GenerateTestToken()
        => GenerateTestToken(TestUserId, new[] { "FarmManager" }, "testuser@test.com");

    public static string GenerateTestToken(Guid userId, IEnumerable<string> roles, string? email = "testuser@test.com")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, email ?? "testuser@test.com"),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("TestSecretKeyThatIsAtLeast32BytesLong!!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
