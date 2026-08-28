namespace FMS.Mobile.Views;
public partial class SettingsPage : ContentPage
{
    public SettingsPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.SettingsViewModel vm)
            vm.LoadSettingsCommand.Execute(null);
    }
}
