using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FMS.Mobile.Helpers;

namespace FMS.Mobile.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenService _tokenService;
    private readonly Func<Task> _onAuthFailure;
    private bool _isRefreshing;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HttpClient Client => _httpClient;

    public ApiService(ITokenService tokenService, Func<Task> onAuthFailure)
    {
        _tokenService = tokenService;
        _onAuthFailure = onAuthFailure;
        _httpClient = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
    }

    public void SetFarmHeader(Guid farmId)
    {
        if (_httpClient.DefaultRequestHeaders.Contains("X-Farm-Id"))
            _httpClient.DefaultRequestHeaders.Remove("X-Farm-Id");
        _httpClient.DefaultRequestHeaders.Add("X-Farm-Id", farmId.ToString());
    }

    public void SetBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        if (!trimmed.StartsWith("http://") && !trimmed.StartsWith("https://"))
            trimmed = "https://" + trimmed;

        AppConfig.BaseUrl = trimmed; // persists for next launch
        _httpClient.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
    }

    private async Task EnsureAuthHeaderAsync()
    {
        var token = await _tokenService.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static string NormalizeEndpoint(string endpoint) => endpoint.TrimStart('/');

    private async Task<bool> TryRefreshTokenAsync()
    {
        if (_isRefreshing) return false;
        await _refreshLock.WaitAsync();
        try
        {
            _isRefreshing = true;
            var refreshToken = await _tokenService.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken)) return false;

            using var refreshClient = new HttpClient { BaseAddress = new Uri(AppConfig.ApiBaseUrl) };
            refreshClient.DefaultRequestHeaders.Add("bypass-tunnel-reminder", "true");
            var response = await refreshClient.PostAsJsonAsync("auth/refresh", new { refreshToken }, JsonOptions);
            if (!response.IsSuccessStatusCode) return false;

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
            if (authResponse is null) return false;

            await _tokenService.SaveTokensAsync(
                authResponse.AccessToken, authResponse.RefreshToken,
                authResponse.UserId, authResponse.AccountId,
                authResponse.Email, authResponse.FirstName, authResponse.LastName);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _isRefreshing = false;
            _refreshLock.Release();
        }
    }

    private async Task<T?> SendWithRetryAsync<T>(Func<HttpClient, Task<HttpResponseMessage>> sendFunc)
    {
        await EnsureAuthHeaderAsync();
        var response = await sendFunc(_httpClient);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshTokenAsync();
            if (refreshed)
                response = await sendFunc(_httpClient);
            else
            {
                await _onAuthFailure();
                return default;
            }
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return default;

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content)) return default;
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private async Task SendWithRetryAsync(Func<HttpClient, Task<HttpResponseMessage>> sendFunc)
    {
        await EnsureAuthHeaderAsync();
        var response = await sendFunc(_httpClient);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshTokenAsync();
            if (refreshed)
                response = await sendFunc(_httpClient);
            else
            {
                await _onAuthFailure();
                return;
            }
        }

        response.EnsureSuccessStatusCode();
    }

    public Task<T?> GetAsync<T>(string endpoint) =>
        SendWithRetryAsync<T>(client => client.GetAsync(NormalizeEndpoint(endpoint)));

    public Task<T?> PostAsync<T>(string endpoint, object? body = null) =>
        SendWithRetryAsync<T>(client =>
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return client.PostAsync(NormalizeEndpoint(endpoint), content);
        });

    public Task PostAsync(string endpoint, object? body = null) =>
        SendWithRetryAsync(client =>
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return client.PostAsync(NormalizeEndpoint(endpoint), content);
        });

    public Task<T?> PutAsync<T>(string endpoint, object? body = null) =>
        SendWithRetryAsync<T>(client =>
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return client.PutAsync(NormalizeEndpoint(endpoint), content);
        });

    public Task DeleteAsync(string endpoint) =>
        SendWithRetryAsync(client => client.DeleteAsync(NormalizeEndpoint(endpoint)));

    public async Task<T?> PostMultipartAsync<T>(string endpoint, Stream fileStream, string fileName, string contentType, Dictionary<string, string>? formFields = null)
    {
        await EnsureAuthHeaderAsync();

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        if (formFields is not null)
        {
            foreach (var kv in formFields)
                content.Add(new StringContent(kv.Value), kv.Key);
        }

        var response = await _httpClient.PostAsync(NormalizeEndpoint(endpoint), content);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshTokenAsync();
            if (refreshed)
            {
                await EnsureAuthHeaderAsync();
                fileStream.Position = 0;
                response = await _httpClient.PostAsync(NormalizeEndpoint(endpoint), content);
            }
            else
            {
                await _onAuthFailure();
                return default;
            }
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    // Internal DTO matching the API auth response
    private class AuthResponse
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
