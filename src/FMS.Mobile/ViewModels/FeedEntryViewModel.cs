using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AnimalId), "animalId")]
public partial class FeedEntryViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly IReferenceDataService _refData;

    [ObservableProperty] private Guid? _animalId;
    [ObservableProperty] private CachedReferenceData? _selectedFeedType;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private DateTime _fedAt = DateTime.Now;
    [ObservableProperty] private string? _notes;

    public ObservableCollection<CachedReferenceData> FeedTypes { get; } = new();

    public FeedEntryViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync, IReferenceDataService refData)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        _refData = refData;
        Title = "Record Feeding";
    }

    [RelayCommand]
    private async Task LoadFeedTypesAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        FeedTypes.Clear();
        var types = await _refData.GetFeedTypesAsync(farmId.Value);
        foreach (var t in types) FeedTypes.Add(t);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Quantity <= 0)
        {
            await Shell.Current.DisplayAlert("Validation", "Quantity must be positive", "OK");
            return;
        }

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);
            var payload = new
            {
                feedTypeId = SelectedFeedType?.DataId ?? Guid.Empty,
                animalId = AnimalId,
                quantity = Quantity,
                fedAt = FedAt,
                notes = Notes
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/feed/records", payload);
                await Shell.Current.DisplayAlert("Saved", "Feed record saved", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "FeedRecord",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Feed record will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}
