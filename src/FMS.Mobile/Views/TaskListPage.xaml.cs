namespace FMS.Mobile.Views;
public partial class TaskListPage : ContentPage
{
    public TaskListPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.TaskListViewModel vm)
            vm.LoadTasksCommand.Execute(null);
    }
}
