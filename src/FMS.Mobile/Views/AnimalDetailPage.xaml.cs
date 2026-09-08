using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class AnimalDetailPage : ContentPage
{
    public AnimalDetailPage(AnimalDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
