using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class BreedingEntryPage : ContentPage
{
    public BreedingEntryPage(BreedingEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is BreedingEntryViewModel vm)
            vm.LoadAnimalsCommand.Execute(null);
    }
}
