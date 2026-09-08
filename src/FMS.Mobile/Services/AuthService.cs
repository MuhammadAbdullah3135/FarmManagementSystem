using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FMS.Mobile.Helpers;
using Microsoft.Extensions.Logging;

namespace FMS.Mobile.Services;

public class AuthService : IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly IDatabaseService _databaseService;
    private readonly IApiService _apiService;
    private readonly ILogger<AuthService> _logger;
    private Guid? _activeFarmId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public Guid? ActiveFarmId => _activeFarmId;
    public bool IsAuthenticated => _activeFarmId.HasValue;
    public string? LastErrorMessage { get; private set; }

    public AuthService(ITokenService tokenService, IDatabaseService databaseService,
                       IApiService apiService, ILogger<AuthService> logger)
    {
        _tokenService = tokenService;
        _databaseService = databaseService;
        _apiService = apiService;
        _logger = logger;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        LastErrorMessage = null;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            client.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
            var response = await client.PostAsJsonAsync("auth/login", new { email, password }, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                LastErrorMessage = $"Server returned {(int)response.StatusCode}: {body}";
                _logger.LogWarning("Mobile login failed with HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode, body);
                return false;
            }

            var authResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (authResponse is null)
            {
                LastErrorMessage = "The server returned an invalid login response.";
                return false;
            }

            await _tokenService.SaveTokensAsync(
                authResponse.AccessToken, authResponse.RefreshToken,
                authResponse.UserId, authResponse.AccountId,
                authResponse.Email, authResponse.FirstName, authResponse.LastName);

            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "Mobile login request failed against {ApiBaseUrl}", AppConfig.ApiBaseUrl);
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string email, string password, string firstName, string lastName, string accountName)
    {
        LastErrorMessage = null;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            client.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
            var response = await client.PostAsJsonAsync("auth/register", new
            {
                email,
                password,
                firstName,
                lastName,
                accountName
            }, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                LastErrorMessage = $"Server returned {(int)response.StatusCode}: {body}";
                _logger.LogWarning("Mobile registration failed with HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode, body);
                return false;
            }

            var authResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (authResponse is null)
            {
                LastErrorMessage = "The server returned an invalid registration response.";
                return false;
            }

            await _tokenService.SaveTokensAsync(
                authResponse.AccessToken, authResponse.RefreshToken,
                authResponse.UserId, authResponse.AccountId,
                authResponse.Email, authResponse.FirstName, authResponse.LastName);

            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "Mobile registration request failed against {ApiBaseUrl}", AppConfig.ApiBaseUrl);
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
            client.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await client.GetAsync("auth/me");
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
        catch (Exception ex)
        {
            LastErrorMessage = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "Loading farms failed against {ApiBaseUrl}", AppConfig.ApiBaseUrl);
            return new List<FarmDto>();
        }
    }

    public async Task<(List<FarmDto> farms, string? diagnostic)> GetUserFarmsAsyncWithDiagnostics()
    {
        var tokenPresent = !string.IsNullOrEmpty(await _tokenService.GetAccessTokenAsync());

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            client.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var token = await _tokenService.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync("farms");
            }
            catch (Exception ex)
            {
                var diag = BuildDiagnostic("network_error", null, null,
                    ex.Message, ex.GetType().FullName, tokenPresent);
                _logger.LogError(ex, "Farm list network error against {ApiBaseUrl}", AppConfig.ApiBaseUrl);
                return (new List<FarmDto>(), diag);
            }

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var diag = BuildDiagnostic("api_error", (int)response.StatusCode,
                    body, null, null, tokenPresent);
                _logger.LogWarning("Farm list API error {StatusCode} against {ApiBaseUrl}: {Body}",
                    (int)response.StatusCode, AppConfig.ApiBaseUrl,
                    body.Length > 500 ? body[..500] : body);
                return (new List<FarmDto>(), diag);
            }

            List<FarmDto>? farms;
            try
            {
                farms = JsonSerializer.Deserialize<List<FarmDto>>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                var diag = BuildDiagnostic("deserialization", (int)response.StatusCode,
                    body, ex.Message, ex.GetType().FullName, tokenPresent);
                _logger.LogError(ex, "Farm list deserialization failed against {ApiBaseUrl}. Response: {Body}",
                    AppConfig.ApiBaseUrl, body.Length > 500 ? body[..500] : body);
                return (new List<FarmDto>(), diag);
            }

            if (farms is null)
            {
                var diag = BuildDiagnostic("empty_response", (int)response.StatusCode,
                    body, "Deserialize returned null", "JsonException", tokenPresent);
                return (new List<FarmDto>(), diag);
            }

            return (farms, null);
        }
        catch (Exception ex)
        {
            var diag = BuildDiagnostic("unexpected", null, null,
                ex.Message, ex.GetType().FullName, tokenPresent);
            _logger.LogError(ex, "Unexpected error loading farms against {ApiBaseUrl}", AppConfig.ApiBaseUrl);
            return (new List<FarmDto>(), diag);
        }
    }

    private static string BuildDiagnostic(
        string phase, int? statusCode, string? responseBody,
        string? exceptionMsg, string? exceptionType, bool tokenPresent)
    {
        var lines = new List<string>
        {
            $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Phase: {phase}",
            $"Base URL: {AppConfig.ApiBaseUrl}",
            $"Tunnel: {AppConfig.BaseUrl}",
            $"Token present: {tokenPresent}",
        };

        if (statusCode.HasValue)
            lines.Add($"HTTP status: {statusCode.Value}");
        else
            lines.Add($"HTTP status: none (no response received)");

        if (!string.IsNullOrEmpty(responseBody))
        {
            var truncated = responseBody.Length > 500 ? responseBody[..500] + "…" : responseBody;
            lines.Add($"Response body: {truncated}");
        }

        if (!string.IsNullOrEmpty(exceptionMsg))
            lines.Add($"Error: {exceptionMsg}");

        if (!string.IsNullOrEmpty(exceptionType))
            lines.Add($"Type: {exceptionType}");

        return string.Join(Environment.NewLine, lines);
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
