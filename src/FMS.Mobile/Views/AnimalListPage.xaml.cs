using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class AnimalListPage : ContentPage
{
    public AnimalListPage(AnimalListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is AnimalListViewModel vm)
            vm.LoadAnimalsCommand.Execute(null);
    }
}
