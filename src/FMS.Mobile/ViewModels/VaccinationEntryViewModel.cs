using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AnimalId), "animalId")]
[QueryProperty(nameof(AnimalTag), "animalTag")]
public partial class VaccinationEntryViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly IReferenceDataService _refData;

    [ObservableProperty] private Guid _animalId;
    [ObservableProperty] private string? _animalTag;
    [ObservableProperty] private CachedReferenceData? _selectedVaccineType;
    [ObservableProperty] private DateTime _dateGiven = DateTime.Now;
    [ObservableProperty] private string? _vetName;
    [ObservableProperty] private string? _batchNumber;
    [ObservableProperty] private decimal _cost;
    [ObservableProperty] private string? _notes;

    public ObservableCollection<CachedReferenceData> VaccineTypes { get; } = new();

    public VaccinationEntryViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync, IReferenceDataService refData)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        _refData = refData;
        Title = "Record Vaccination";
    }

    [RelayCommand]
    private async Task LoadVaccineTypesAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        VaccineTypes.Clear();
        var types = await _refData.GetVaccineTypesAsync(farmId.Value);
        foreach (var t in types) VaccineTypes.Add(t);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedVaccineType == null)
        {
            await Shell.Current.DisplayAlert("Validation", "Please select a vaccine type", "OK");
            return;
        }

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);
            var payload = new
            {
                animalId = AnimalId,
                vaccineTypeId = SelectedVaccineType.DataId,
                dateGiven = DateGiven.ToString("yyyy-MM-dd"),
                vetName = VetName,
                batchNumber = BatchNumber,
                cost = Cost,
                notes = Notes
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/vaccinations", payload);
                await Shell.Current.DisplayAlert("Saved", "Vaccination record saved", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "VaccinationRecord",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Vaccination will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}
