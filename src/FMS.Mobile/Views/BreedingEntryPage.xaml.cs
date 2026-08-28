namespace FMS.Mobile.Views;

public partial class BreedingEntryPage : ContentPage
{
    public BreedingEntryPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.BreedingEntryViewModel vm)
            vm.LoadAnimalsCommand.Execute(null);
    }
}
