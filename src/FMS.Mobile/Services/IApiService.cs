namespace FMS.Mobile.Services;

public interface IApiService
{
    HttpClient Client { get; }
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object? body = null);
    Task PostAsync(string endpoint, object? body = null);
    Task<T?> PutAsync<T>(string endpoint, object? body = null);
    Task DeleteAsync(string endpoint);
    Task<T?> PostMultipartAsync<T>(string endpoint, Stream fileStream, string fileName, string contentType, Dictionary<string, string>? formFields = null);
    void SetFarmHeader(Guid farmId);

    /// <summary>
    /// Re-points the shared HttpClient at a new API origin (and persists it).
    /// </summary>
    void SetBaseUrl(string baseUrl);
}
