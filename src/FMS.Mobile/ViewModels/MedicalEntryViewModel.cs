using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AnimalId), "animalId")]
[QueryProperty(nameof(AnimalTag), "animalTag")]
public partial class MedicalEntryViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;

    [ObservableProperty] private Guid _animalId;
    [ObservableProperty] private string? _animalTag;
    [ObservableProperty] private string _symptoms = string.Empty;
    [ObservableProperty] private string? _diagnosis;
    [ObservableProperty] private string? _treatment;
    [ObservableProperty] private string? _medicineUsed;
    [ObservableProperty] private string? _dosage;
    [ObservableProperty] private string? _vetName;
    [ObservableProperty] private decimal _cost;
    [ObservableProperty] private DateTime _dateRecorded = DateTime.Now;
    [ObservableProperty] private DateTime? _followUpDate;
    [ObservableProperty] private string _selectedStatus = "Open";
    [ObservableProperty] private string? _notes;

    public ObservableCollection<string> Statuses { get; } = new()
    {
        "Open", "InProgress", "Resolved"
    };

    public MedicalEntryViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        Title = "Medical Entry";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Symptoms))
        {
            await Shell.Current.DisplayAlert("Validation", "Symptoms are required", "OK");
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
                symptoms = Symptoms,
                diagnosis = Diagnosis,
                treatment = Treatment,
                medicineUsed = MedicineUsed,
                dosage = Dosage,
                vetName = VetName,
                cost = Cost,
                dateRecorded = DateRecorded.ToString("yyyy-MM-dd"),
                followUpDate = FollowUpDate?.ToString("yyyy-MM-dd"),
                status = SelectedStatus,
                notes = Notes
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/medical-records", payload);
                await Shell.Current.DisplayAlert("Saved", "Medical record saved", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "MedicalRecord",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Medical record will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}
