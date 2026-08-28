namespace FMS.Mobile.Helpers;

public static class AppConfig
{
#if DEBUG
    private static string _baseUrl = "http://10.0.2.2:5000"; // Android emulator localhost
#else
    private static string _baseUrl = "https://api.yourfarm.com";
#endif

    public static string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value;
    }

    public static string ApiBaseUrl => $"{BaseUrl}/api";
}
