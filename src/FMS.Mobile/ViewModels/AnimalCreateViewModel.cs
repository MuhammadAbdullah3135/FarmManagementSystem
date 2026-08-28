using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class AnimalCreateViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;

    [ObservableProperty] private string _tagNumber = string.Empty;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _selectedTypeName;
    [ObservableProperty] private string? _selectedBreedName;
    [ObservableProperty] private DateTime _dateOfBirth = DateTime.Today;
    [ObservableProperty] private string? _notes;

    public AnimalCreateViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        Title = "Register Animal";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(TagNumber))
        {
            await Shell.Current.DisplayAlert("Validation", "Tag number is required", "OK");
            return;
        }

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);

            var payload = new
            {
                tagNumber = TagNumber,
                name = Name,
                dateOfBirth = DateOfBirth,
                notes = Notes,
                // These would normally come from dropdowns; simplified for mobile
                animalTypeId = Guid.Empty, // TODO: wire up dropdowns
                sexOptionId = Guid.Empty,
                animalStatusId = Guid.Empty
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/animals", payload);
                await Shell.Current.DisplayAlert("Success", "Animal registered", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                // Queue offline
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "Animal",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);

                await Shell.Current.DisplayAlert("Queued", "Animal will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}
