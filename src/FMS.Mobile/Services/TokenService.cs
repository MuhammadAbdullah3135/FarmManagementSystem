namespace FMS.Mobile.Services;

public class TokenService : ITokenService
{
    private const string AccessTokenKey = "fms_access_token";
    private const string RefreshTokenKey = "fms_refresh_token";
    private const string UserIdKey = "fms_user_id";
    private const string AccountIdKey = "fms_account_id";
    private const string EmailKey = "fms_email";
    private const string FirstNameKey = "fms_first_name";
    private const string LastNameKey = "fms_last_name";
    private const string ActiveFarmIdKey = "fms_active_farm_id";

    public async Task SaveTokensAsync(string accessToken, string refreshToken, Guid userId, Guid accountId, string email, string firstName, string lastName)
    {
        await SecureStorage.Default.SetAsync(AccessTokenKey, accessToken);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        await SecureStorage.Default.SetAsync(UserIdKey, userId.ToString());
        await SecureStorage.Default.SetAsync(AccountIdKey, accountId.ToString());
        await SecureStorage.Default.SetAsync(EmailKey, email);
        await SecureStorage.Default.SetAsync(FirstNameKey, firstName);
        await SecureStorage.Default.SetAsync(LastNameKey, lastName);
    }

    public Task<string?> GetAccessTokenAsync() => SecureStorage.Default.GetAsync(AccessTokenKey);
    public Task<string?> GetRefreshTokenAsync() => SecureStorage.Default.GetAsync(RefreshTokenKey);
    public Task<string?> GetEmailAsync() => SecureStorage.Default.GetAsync(EmailKey);
    public Task<string?> GetFirstNameAsync() => SecureStorage.Default.GetAsync(FirstNameKey);

    public async Task<Guid?> GetUserIdAsync()
    {
        var val = await SecureStorage.Default.GetAsync(UserIdKey);
        return Guid.TryParse(val, out var id) ? id : null;
    }

    public async Task<Guid?> GetAccountIdAsync()
    {
        var val = await SecureStorage.Default.GetAsync(AccountIdKey);
        return Guid.TryParse(val, out var id) ? id : null;
    }

    public async Task SaveActiveFarmIdAsync(Guid farmId)
    {
        await SecureStorage.Default.SetAsync(ActiveFarmIdKey, farmId.ToString());
    }

    public async Task<Guid?> GetActiveFarmIdAsync()
    {
        var val = await SecureStorage.Default.GetAsync(ActiveFarmIdKey);
        return Guid.TryParse(val, out var id) ? id : null;
    }

    public Task ClearAllAsync()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        SecureStorage.Default.Remove(UserIdKey);
        SecureStorage.Default.Remove(AccountIdKey);
        SecureStorage.Default.Remove(EmailKey);
        SecureStorage.Default.Remove(FirstNameKey);
        SecureStorage.Default.Remove(LastNameKey);
        SecureStorage.Default.Remove(ActiveFarmIdKey);
        return Task.CompletedTask;
    }

    public async Task<bool> HasTokenAsync()
    {
        var token = await GetAccessTokenAsync();
        return !string.IsNullOrEmpty(token);
    }
}
