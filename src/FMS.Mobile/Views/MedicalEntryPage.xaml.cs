using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class MedicalEntryPage : ContentPage
{
    public MedicalEntryPage(MedicalEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
