using System.Security.Claims;

namespace FMS.Application.Auth;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, Guid accountId, string email, IList<string> roles);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
}
