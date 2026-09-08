using FMS.Mobile.Services;
using FMS.Mobile.ViewModels;
using FMS.Mobile.Views;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace FMS.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Services ──────────────────────────────────────
        builder.Services.AddSingleton<ITokenService, TokenService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();

        // IApiService needs a factory because it requires onAuthFailure callback
        builder.Services.AddSingleton<IApiService>(sp =>
        {
            var tokenService = sp.GetRequiredService<ITokenService>();
            var onAuthFailure = async () =>
            {
                await Shell.Current.GoToAsync("//Login");
            };
            return new ApiService(tokenService, onAuthFailure);
        });

        builder.Services.AddSingleton<IAuthService>(sp =>
            new AuthService(
                sp.GetRequiredService<ITokenService>(),
                sp.GetRequiredService<IDatabaseService>(),
                sp.GetRequiredService<IApiService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuthService>>()));

        builder.Services.AddSingleton<ISyncService>(sp =>
            new SyncService(
                sp.GetRequiredService<IDatabaseService>(),
                sp.GetRequiredService<IApiService>()));

        builder.Services.AddSingleton<IReferenceDataService, ReferenceDataService>();
        builder.Services.AddSingleton<IImageCaptureService, ImageCaptureService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();

        // ── ViewModels ────────────────────────────────────
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<FarmPickerViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AnimalListViewModel>();
        builder.Services.AddTransient<AnimalDetailViewModel>();
        builder.Services.AddTransient<AnimalCreateViewModel>();
        builder.Services.AddTransient<TaskListViewModel>();
        builder.Services.AddTransient<WeightEntryViewModel>();
        builder.Services.AddTransient<FeedEntryViewModel>();
        builder.Services.AddTransient<MedicalEntryViewModel>();
        builder.Services.AddTransient<VaccinationEntryViewModel>();
        builder.Services.AddTransient<BreedingEntryViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // ── Pages ─────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<FarmPickerPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AnimalListPage>();
        builder.Services.AddTransient<AnimalDetailPage>();
        builder.Services.AddTransient<AnimalCreatePage>();
        builder.Services.AddTransient<TaskListPage>();
        builder.Services.AddTransient<WeightEntryPage>();
        builder.Services.AddTransient<FeedEntryPage>();
        builder.Services.AddTransient<MedicalEntryPage>();
        builder.Services.AddTransient<VaccinationEntryPage>();
        builder.Services.AddTransient<BreedingEntryPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ScannerPage>();

        // ── Converters ────────────────────────────────────
        builder.Services.AddSingleton<Converters.InvertBoolConverter>();
        builder.Services.AddSingleton<Converters.IsNotNullConverter>();
        builder.Services.AddSingleton<Converters.NullToEmptyConverter>();
        builder.Services.AddSingleton<Converters.IsNonZeroConverter>();

        // Keep authentication/network diagnostics available through Android logcat
        // in both Debug and Release builds during remote testing.
        builder.Logging.AddDebug();

        return builder.Build();
    }
}
