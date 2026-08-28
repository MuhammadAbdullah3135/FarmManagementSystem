namespace FMS.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("AnimalDetail", typeof(Views.AnimalDetailPage));
        Routing.RegisterRoute("AnimalCreate", typeof(Views.AnimalCreatePage));
        Routing.RegisterRoute("WeightEntry", typeof(Views.WeightEntryPage));
        Routing.RegisterRoute("MedicalEntry", typeof(Views.MedicalEntryPage));
        Routing.RegisterRoute("VaccinationEntry", typeof(Views.VaccinationEntryPage));
        Routing.RegisterRoute("BreedingEntry", typeof(Views.BreedingEntryPage));
        Routing.RegisterRoute("Scanner", typeof(Views.ScannerPage));
    }
}
