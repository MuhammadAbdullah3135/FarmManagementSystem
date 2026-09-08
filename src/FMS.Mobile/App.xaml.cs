using FMS.Mobile.Services;

namespace FMS.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IAuthService _authService;
    private readonly IDatabaseService _databaseService;
    private readonly ISyncService _syncService;
    private readonly IConnectivityService _connectivityService;
    private readonly ICrashLogService _crashLog;
    private readonly IServiceProvider _services;

    public App(IAuthService authService, IDatabaseService databaseService,
               ISyncService syncService, IConnectivityService connectivityService,
               ICrashLogService crashLog, IServiceProvider services)
    {
        InitializeComponent();
        _authService = authService;
        _databaseService = databaseService;
        _syncService = syncService;
        _connectivityService = connectivityService;
        _crashLog = crashLog;
        _services = services;

        // SQLite init is fire-and-forget; route failures to the crash log
        // instead of letting them become silent unobserved exceptions.
        _ = InitializeDatabaseAsync();

        _connectivityService.ConnectivityChanged += async (_, connected) =>
        {
            if (connected && _authService.ActiveFarmId.HasValue)
                await _syncService.SyncPendingRecordsAsync(_authService.ActiveFarmId.Value);
        };
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await _databaseService.InitializeAsync();
            _crashLog.LogEvent("Database initialized");
        }
        catch (Exception ex)
        {
            _crashLog.LogCrash("DatabaseService.InitializeAsync", ex);
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new AppShell(_services));

    protected override async void OnStart()
    {
        base.OnStart();
        var restored = await _authService.RestoreSessionAsync();
        if (restored && _authService.ActiveFarmId.HasValue)
            await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync("//Main/Dashboard"));
        else if (restored)
            await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync("//FarmPicker"));
    }
}
