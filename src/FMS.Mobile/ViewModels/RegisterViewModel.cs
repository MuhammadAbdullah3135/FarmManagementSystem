using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _accountName = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public RegisterViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = "Create Account";
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            return;
        }
        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            return;
        }
        if (!string.Equals(Password, ConfirmPassword))
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            ErrorMessage = "First and last name are required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            ErrorMessage = "Account / farm name is required.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var success = await _authService.RegisterAsync(
                Email.Trim(), Password, FirstName.Trim(), LastName.Trim(), AccountName.Trim());

            if (success)
            {
                // Registered and auto-logged-in; go pick/create a farm.
                await Shell.Current.GoToAsync("//FarmPicker");
            }
            else
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Registration failed.";
            }
        });
    }

    [RelayCommand]
    private Task BackToLoginAsync() => Shell.Current.GoToAsync("..");
}
