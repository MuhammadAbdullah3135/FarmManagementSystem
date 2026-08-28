using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AnimalId), "animalId")]
public partial class WeightEntryViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;

    [ObservableProperty] private Guid _animalId;
    [ObservableProperty] private decimal _weightKg;
    [ObservableProperty] private DateTime _recordedAt = DateTime.Now;
    [ObservableProperty] private string? _notes;

    public WeightEntryViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        Title = "Record Weight";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (WeightKg <= 0)
        {
            await Shell.Current.DisplayAlert("Validation", "Weight must be positive", "OK");
            return;
        }

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);

            var payload = new
            {
                weightKg = WeightKg,
                recordedAt = RecordedAt,
                notes = Notes
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/animals/{AnimalId}/weights", payload);
                await Shell.Current.DisplayAlert("Saved", $"Weight {WeightKg} kg recorded", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "WeightRecord",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        animalId = AnimalId,
                        weightKg = WeightKg,
                        recordedAt = RecordedAt,
                        notes = Notes
                    }),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Weight will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}
