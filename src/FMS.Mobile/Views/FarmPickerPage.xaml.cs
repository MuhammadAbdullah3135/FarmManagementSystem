using FMS.Mobile.Services;
using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class FarmPickerPage : ContentPage
{
    public FarmPickerPage(FarmPickerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnFarmSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FarmDto farm)
            return;

        if (BindingContext is FarmPickerViewModel vm)
            await vm.SelectFarmCommand.ExecuteAsync(farm);

        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is FarmPickerViewModel vm)
            vm.LoadFarmsCommand.Execute(null);
    }
}
