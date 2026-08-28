using System.Net.Http.Json;
using System.Text.Json;
using FMS.Mobile.Helpers;

namespace FMS.Mobile.Services;

public class AuthService : IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly IDatabaseService _databaseService;
    private readonly IApiService _apiService;
    private Guid? _activeFarmId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public Guid? ActiveFarmId => _activeFarmId;
    public bool IsAuthenticated => _activeFarmId.HasValue;

    public AuthService(ITokenService tokenService, IDatabaseService databaseService, IApiService apiService)
    {
        _tokenService = tokenService;
        _databaseService = databaseService;
        _apiService = apiService;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, JsonOptions);
            if (!response.IsSuccessStatusCode) return false;

            var authResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (authResponse is null) return false;

            await _tokenService.SaveTokensAsync(
                authResponse.AccessToken, authResponse.RefreshToken,
                authResponse.UserId, authResponse.AccountId,
                authResponse.Email, authResponse.FirstName, authResponse.LastName);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        await _tokenService.ClearAllAsync();
        await _databaseService.ClearAllAsync();
        _activeFarmId = null;
    }

    public async Task<bool> RestoreSessionAsync()
    {
        if (!await _tokenService.HasTokenAsync()) return false;

        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return false;

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await client.GetAsync("/auth/me");
            if (!response.IsSuccessStatusCode) return false;

            // Token is valid, restore farm selection
            var farmId = await _tokenService.GetActiveFarmIdAsync();
            if (farmId.HasValue)
            {
                _activeFarmId = farmId;
                _apiService.SetFarmHeader(farmId.Value);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<FarmDto>> GetUserFarmsAsync()
    {
        try
        {
            var farms = await _apiService.GetAsync<List<FarmDto>>("/farms");
            return farms ?? new List<FarmDto>();
        }
        catch
        {
            return new List<FarmDto>();
        }
    }

    public async Task SelectFarmAsync(Guid farmId)
    {
        _activeFarmId = farmId;
        await _tokenService.SaveActiveFarmIdAsync(farmId);
        _apiService.SetFarmHeader(farmId);
    }

    private class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public Guid AccountId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
