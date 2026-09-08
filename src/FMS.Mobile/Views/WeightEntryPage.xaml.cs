using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class WeightEntryPage : ContentPage
{
    public WeightEntryPage(WeightEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
