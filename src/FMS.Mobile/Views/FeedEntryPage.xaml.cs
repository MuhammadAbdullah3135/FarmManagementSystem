namespace FMS.Mobile.Views;
public partial class FeedEntryPage : ContentPage
{
    public FeedEntryPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.FeedEntryViewModel vm)
            vm.LoadFeedTypesCommand.Execute(null);
    }
}
