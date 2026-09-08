using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class VaccinationEntryPage : ContentPage
{
    public VaccinationEntryPage(VaccinationEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is VaccinationEntryViewModel vm)
            vm.LoadVaccineTypesCommand.Execute(null);
    }
}
