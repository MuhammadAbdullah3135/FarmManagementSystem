namespace FMS.Mobile.Views;

public partial class DashboardPage : ContentPage
{
    public DashboardPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.DashboardViewModel vm)
            vm.LoadDashboardCommand.Execute(null);
    }
}
