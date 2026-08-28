namespace FMS.Mobile.Views;

public partial class AnimalListPage : ContentPage
{
    public AnimalListPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.AnimalListViewModel vm)
            vm.LoadAnimalsCommand.Execute(null);
    }
}
