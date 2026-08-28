namespace FMS.Mobile.Views;
public partial class VaccinationEntryPage : ContentPage
{
    public VaccinationEntryPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.VaccinationEntryViewModel vm)
            vm.LoadVaccineTypesCommand.Execute(null);
    }
}
