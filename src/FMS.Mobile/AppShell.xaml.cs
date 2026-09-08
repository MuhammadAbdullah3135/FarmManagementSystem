namespace FMS.Mobile;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        LoginShell.Content = services.GetRequiredService<Views.LoginPage>();
        FarmPickerShell.Content = services.GetRequiredService<Views.FarmPickerPage>();

        // Shell templates otherwise call parameterless constructors and bypass DI ViewModels.
        // Assign every page through the container so bindings and OnAppearing loaders are active.
        DashboardShell.Content = services.GetRequiredService<Views.DashboardPage>();
        AnimalListShell.Content = services.GetRequiredService<Views.AnimalListPage>();
        TaskListShell.Content = services.GetRequiredService<Views.TaskListPage>();
        FeedShell.Content = services.GetRequiredService<Views.FeedEntryPage>();
        SettingsShell.Content = services.GetRequiredService<Views.SettingsPage>();
        AnimalDetailShell.Content = services.GetRequiredService<Views.AnimalDetailPage>();
        AnimalCreateShell.Content = services.GetRequiredService<Views.AnimalCreatePage>();
        WeightEntryShell.Content = services.GetRequiredService<Views.WeightEntryPage>();
        FeedEntryRouteShell.Content = services.GetRequiredService<Views.FeedEntryPage>();
        MedicalEntryShell.Content = services.GetRequiredService<Views.MedicalEntryPage>();
        VaccinationEntryShell.Content = services.GetRequiredService<Views.VaccinationEntryPage>();
        BreedingEntryShell.Content = services.GetRequiredService<Views.BreedingEntryPage>();
        ScannerShell.Content = services.GetRequiredService<Views.ScannerPage>();
    }
}
