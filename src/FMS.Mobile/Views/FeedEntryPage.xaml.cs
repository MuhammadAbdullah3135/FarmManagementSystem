using FMS.Mobile.ViewModels;

namespace FMS.Mobile.Views;

public partial class FeedEntryPage : ContentPage
{
    public FeedEntryPage(FeedEntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is FeedEntryViewModel vm)
            vm.LoadFeedTypesCommand.Execute(null);
    }
}
