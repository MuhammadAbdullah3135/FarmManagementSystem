using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = "Login";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter email and password";
            return;
        }

        await RunBusyAsync(async () =>
        {
            ErrorMessage = string.Empty;
            var success = await _authService.LoginAsync(Email, Password);
            if (success)
            {
                await Shell.Current.GoToAsync("//FarmPicker");
            }
            else
            {
                // Show the real reason (connection error, HTTP status, etc.)
                // rather than a generic message that hides the actual problem.
                ErrorMessage = _authService.LastErrorMessage ?? "Invalid email or password";
            }
        });
    }
}
