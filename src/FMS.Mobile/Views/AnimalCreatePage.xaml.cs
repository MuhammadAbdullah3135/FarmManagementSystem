using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class AnimalCreatePage : ContentPage
{
    public AnimalCreatePage(AnimalCreateViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
