using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;
    private readonly IDatabaseService _db;
    private readonly ITokenService _tokenService;
    private readonly ICrashLogService _crashLog;

    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty] private string _userEmail = string.Empty;
    [ObservableProperty] private string _currentFarmName = string.Empty;
    [ObservableProperty] private DateTime? _lastSyncTime;
    [ObservableProperty] private int _pendingRecords;
    [ObservableProperty] private string _crashLogText = string.Empty;
    [ObservableProperty] private bool _isCrashLogVisible;

    public SettingsViewModel(IAuthService auth, ISyncService sync, IDatabaseService db, ITokenService tokenService, ICrashLogService crashLog)
    {
        _auth = auth;
        _sync = sync;
        _db = db;
        _tokenService = tokenService;
        _crashLog = crashLog;
        Title = "Settings";
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        UserName = (await _tokenService.GetFirstNameAsync()) ?? "User";
        UserEmail = (await _tokenService.GetEmailAsync()) ?? "";
        LastSyncTime = _sync.LastSyncTime;
        var farmId = _auth.ActiveFarmId;
        if (farmId.HasValue)
        {
            PendingRecords = await _db.GetPendingCountAsync(farmId.Value);
            var farms = await _auth.GetUserFarmsAsync();
            CurrentFarmName = farms.FirstOrDefault(f => f.Id == farmId.Value)?.Name ?? "Unknown";
        }
    }

    [RelayCommand]
    private async Task SwitchFarmAsync()
    {
        await Shell.Current.GoToAsync("//FarmPicker");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirm = await Shell.Current.DisplayAlert("Logout",
            "This will clear all local data including unsynced records. Continue?",
            "Logout", "Cancel");

        if (!confirm) return;

        await _auth.LogoutAsync();
        await Shell.Current.GoToAsync("//Login");
    }

    [RelayCommand]
    private async Task ToggleCrashLogAsync()
    {
        IsCrashLogVisible = !IsCrashLogVisible;
        if (IsCrashLogVisible)
            CrashLogText = await _crashLog.ReadLogAsync();
    }

    [RelayCommand]
    private async Task CopyCrashLogAsync()
    {
        var text = CrashLogText;
        if (string.IsNullOrWhiteSpace(text) || text == "(log is empty)")
        {
            text = await _crashLog.ReadLogAsync();
            CrashLogText = text;
        }
        await Clipboard.Default.SetTextAsync(text);
        await Shell.Current.DisplayAlert("Crash Log", "Log copied to clipboard.", "OK");
    }

    [RelayCommand]
    private Task ShareCrashLogAsync() => _crashLog.ShareLogAsync();

    [RelayCommand]
    private async Task ClearCrashLogAsync()
    {
        var confirm = await Shell.Current.DisplayAlert("Crash Log",
            "Delete the entire crash log?", "Delete", "Cancel");
        if (!confirm) return;
        await _crashLog.ClearLogAsync();
        CrashLogText = "(log is empty)";
    }
}
