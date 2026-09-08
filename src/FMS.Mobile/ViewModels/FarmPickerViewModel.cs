using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class FarmPickerViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<FarmDto> Farms { get; } = new();

    public string EmptyStateText => string.IsNullOrWhiteSpace(ErrorMessage)
        ? "No active farms are available."
        : ErrorMessage;

    public FarmPickerViewModel(IAuthService authService, ITokenService tokenService)
    {
        _authService = authService;
        _tokenService = tokenService;
        Title = "Select Farm";
    }

    [RelayCommand]
    private async Task LoadFarmsAsync()
    {
        try
        {
            await RunBusyAsync(async () =>
            {
                ErrorMessage = string.Empty;
                Farms.Clear();

                var (farms, diagnostic) = await _authService.GetUserFarmsAsyncWithDiagnostics();

                if (diagnostic is not null)
                {
                    ErrorMessage = diagnostic;
                    await AppendErrorLog(diagnostic);
                    await ShowErrorAlert(diagnostic);
                    OnPropertyChanged(nameof(EmptyStateText));
                    return;
                }

                foreach (var farm in farms.Where(f => f.IsActive))
                    Farms.Add(farm);

                if (Farms.Count == 1)
                {
                    // Only one active farm: skip the picker and open it directly so the
                    // user never lands on a screen with nothing to click.
                    var only = Farms[0];
                    await SelectFarmAsync(only);
                    return;
                }

                if (Farms.Count == 0)
                {
                    var fallbackDiag = BuildNoActiveFarmsDiagnostic(farms);
                    ErrorMessage = fallbackDiag;
                    await AppendErrorLog(fallbackDiag);
                    OnPropertyChanged(nameof(EmptyStateText));
                }
            });
        }
        catch (Exception ex)
        {
            var tokenPresent = !string.IsNullOrEmpty(await _tokenService.GetAccessTokenAsync());
            var diagnostic = BuildLoadFarmsUnexpectedDiagnostic(ex, tokenPresent);
            ErrorMessage = diagnostic;
            await AppendErrorLog(diagnostic);
            await ShowErrorAlert(diagnostic);
        }
    }

    [RelayCommand]
    private async Task SelectFarmAsync(FarmDto? farm)
    {
        if (farm is null)
            return;

        try
        {
            ErrorMessage = string.Empty;
            await _authService.SelectFarmAsync(farm.Id);
            await Shell.Current.GoToAsync("//Main/Dashboard");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to open {farm.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        ErrorMessage = string.Empty;
        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("//Login");
    }

    [RelayCommand]
    private async Task CopyErrorAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            await Clipboard.SetTextAsync(ErrorMessage);
        }
    }

    private async Task ShowErrorAlert(string diagnostic)
    {
        try
        {
            await Shell.Current.DisplayAlert("Farm Load Error", diagnostic, "OK");
        }
        catch
        {
            // Alert failure shouldn't break the app
        }
    }

    private static string BuildNoActiveFarmsDiagnostic(List<FarmDto> allFarms)
    {
        var lines = new List<string>
        {
            $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Phase: no_active_farms",
            $"Total farms returned: {allFarms.Count}",
        };

        if (allFarms.Count > 0)
        {
            foreach (var f in allFarms)
                lines.Add($"  - {f.Name} (Id={f.Id}, IsActive={f.IsActive})");
        }

        lines.Add("All returned farms have IsActive=False.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildLoadFarmsUnexpectedDiagnostic(Exception ex, bool tokenPresent)
    {
        var lines = new List<string>
        {
            $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Phase: loadfarms_unexpected",
            $"Base URL: {Helpers.AppConfig.ApiBaseUrl}",
            $"Tunnel: {Helpers.AppConfig.BaseUrl}",
            $"Token present: {tokenPresent}",
            $"Error: {ex.Message}",
            $"Type: {ex.GetType().FullName}",
        };

        if (ex.InnerException is not null)
            lines.Add($"Inner: {ex.InnerException.Message}");

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task AppendErrorLog(string diagnostic)
    {
        try
        {
            var logPath = Path.Combine(FileSystem.AppDataDirectory, "fms_errors.log");

            if (File.Exists(logPath))
            {
                var info = new FileInfo(logPath);
                if (info.Length > 1_048_576) // 1MB cap
                {
                    var lines = await File.ReadAllLinesAsync(logPath);
                    var kept = lines.Skip(Math.Max(0, lines.Length - 200)).ToArray();
                    await File.WriteAllLinesAsync(logPath, kept);
                }
            }

            await File.AppendAllTextAsync(logPath,
                $"{DateTime.Now:O} | {diagnostic}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Log write failure shouldn't break the app
        }
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyStateText));
    }
}
