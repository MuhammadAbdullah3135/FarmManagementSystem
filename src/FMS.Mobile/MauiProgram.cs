using FMS.Mobile.Services;
using Microsoft.Maui.LifecycleEvents;

#if FIREBASE && ANDROID
using Plugin.Firebase.Core.Platforms.Android;
using Plugin.Firebase.Crashlytics;
#endif

namespace FMS.Mobile;

public static class MauiProgram
{
    private const string SiteUrl = "https://muhammadabdullah3135.github.io/FarmManagementSystem/";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<FmsWebView, FmsWebViewHandler>();
            })
            .ConfigureLifecycleEvents(events =>
            {
#if FIREBASE && ANDROID
                events.AddAndroid(android =>
                    android.OnCreate((activity, _) =>
                    {
                        try
                        {
                            CrossFirebase.Initialize(
                                activity,
                                () => Platform.CurrentActivity);
                            CrossFirebaseCrashlytics.Current.SetCrashlyticsCollectionEnabled(true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[FMS] Firebase init failed: {ex.Message}");
                        }
                    })
                );
#endif
            });

        builder.Services.AddSingleton<CrashReporter>();

        return builder.Build();
    }
}
