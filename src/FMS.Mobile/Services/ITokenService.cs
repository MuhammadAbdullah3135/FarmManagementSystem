namespace FMS.Mobile.Services;

public interface ITokenService
{
    Task SaveTokensAsync(string accessToken, string refreshToken, Guid userId, Guid accountId, string email, string firstName, string lastName);
    Task<string?> GetAccessTokenAsync();
    Task<string?> GetRefreshTokenAsync();
    Task<Guid?> GetUserIdAsync();
    Task<Guid?> GetAccountIdAsync();
    Task<string?> GetEmailAsync();
    Task<string?> GetFirstNameAsync();
    Task SaveActiveFarmIdAsync(Guid farmId);
    Task<Guid?> GetActiveFarmIdAsync();
    Task ClearAllAsync();
    Task<bool> HasTokenAsync();
}
