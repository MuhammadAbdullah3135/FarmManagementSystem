using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class FarmPickerViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    public ObservableCollection<FarmDto> Farms { get; } = new();

    public FarmPickerViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = "Select Farm";
    }

    [RelayCommand]
    private async Task LoadFarmsAsync()
    {
        await RunBusyAsync(async () =>
        {
            Farms.Clear();
            var farms = await _authService.GetUserFarmsAsync();
            foreach (var farm in farms.Where(f => f.IsActive))
                Farms.Add(farm);
        });
    }

    [RelayCommand]
    private async Task SelectFarmAsync(FarmDto farm)
    {
        await _authService.SelectFarmAsync(farm.Id);
        await Shell.Current.GoToAsync("//Main/Dashboard");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("//Login");
    }
}
