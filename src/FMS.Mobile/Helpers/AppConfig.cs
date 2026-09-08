using Microsoft.Maui.Storage;

namespace FMS.Mobile.Helpers;

public static class AppConfig
{
#if DEBUG
    private const string DefaultBaseUrl = "http://10.0.2.2:5000"; // Android emulator localhost
#else
    private const string DefaultBaseUrl = "https://shaved-downloadable-owns-watson.trycloudflare.com";
#endif

    private const string CustomBaseUrlKey = "fms_custom_base_url";

    /// <summary>
    /// The API base origin (no trailing slash). Returns the persisted runtime
    /// override when one has been set, otherwise the build-time default. This
    /// lets a user change the tunnel/server URL on the device without rebuilding.
    /// </summary>
    public static string BaseUrl
    {
        get
        {
            var custom = Preferences.Default.Get(CustomBaseUrlKey, string.Empty);
            return string.IsNullOrWhiteSpace(custom)
                ? DefaultBaseUrl
                : custom.Trim().TrimEnd('/');
        }
        set
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
                Preferences.Default.Remove(CustomBaseUrlKey);
            else
                Preferences.Default.Set(CustomBaseUrlKey, trimmed.TrimEnd('/'));
        }
    }

    public static string ApiBaseUrl => $"{BaseUrl.TrimEnd('/')}/api/";
}