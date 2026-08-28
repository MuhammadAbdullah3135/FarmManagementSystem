namespace FMS.Mobile.Views;

public partial class FarmPickerPage : ContentPage
{
    public FarmPickerPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.FarmPickerViewModel vm)
            vm.LoadFarmsCommand.Execute(null);
    }
}
