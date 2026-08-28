using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AnimalId), "id")]
public partial class AnimalDetailViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly IImageCaptureService _imageCapture;

    [ObservableProperty] private Guid _animalId;
    [ObservableProperty] private string _tagNumber = string.Empty;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string _animalType = string.Empty;
    [ObservableProperty] private string? _breed;
    [ObservableProperty] private string _sex = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string? _location;
    [ObservableProperty] private decimal? _latestWeight;
    [ObservableProperty] private DateTime? _dateOfBirth;
    [ObservableProperty] private string? _notes;

    public AnimalDetailViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync, IImageCaptureService imageCapture)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        _imageCapture = imageCapture;
        Title = "Animal Detail";
    }

    partial void OnAnimalIdChanged(Guid value) => _ = LoadAnimalAsync();

    [RelayCommand]
    private async Task LoadAnimalAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue || AnimalId == Guid.Empty) return;

        _api.SetFarmHeader(farmId.Value);
        await RunBusyAsync(async () =>
        {
            try
            {
                var animal = await _api.GetAsync<AnimalDetail>($"/farm/{farmId}/animals/{AnimalId}");
                if (animal is not null)
                {
                    TagNumber = animal.TagNumber;
                    Name = animal.Name;
                    AnimalType = animal.AnimalTypeName;
                    Breed = animal.BreedName;
                    Sex = animal.SexValue;
                    Status = animal.StatusName;
                    Location = animal.LocationName;
                    LatestWeight = animal.LatestWeightKg;
                    DateOfBirth = animal.DateOfBirth;
                    Notes = animal.Notes;
                    Title = $"{animal.TagNumber} - {animal.Name ?? "Unnamed"}";
                }
            }
            catch { /* show what we have */ }
        });
    }

    [RelayCommand]
    private async Task AddPhotoAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            var filePath = await _imageCapture.CaptureAndCompressAsync();
            if (string.IsNullOrEmpty(filePath)) return; // user cancelled

            _api.SetFarmHeader(farmId.Value);
            try
            {
                // Online: upload directly
                using var stream = File.OpenRead(filePath);
                var result = await _api.PostMultipartAsync<ImageUploadResult>(
                    $"/farm/{farmId}/animals/{AnimalId}/images",
                    stream,
                    Path.GetFileName(filePath),
                    "image/jpeg",
                    new Dictionary<string, string> { ["caption"] = "", ["isPrimary"] = "false" });

                await Shell.Current.DisplayAlert("Uploaded", "Photo uploaded successfully", "OK");

                // Clean up local file
                try { File.Delete(filePath); } catch { }
            }
            catch
            {
                // Offline: queue for sync
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "Image",
                    FarmId = farmId.Value,
                    FilePath = filePath,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new { animalId = AnimalId, caption = "" }),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Photo will upload when online", "OK");
            }
        });
    }

    [RelayCommand]
    private async Task GoToWeightEntryAsync() =>
        await Shell.Current.GoToAsync($"WeightEntry?animalId={AnimalId}");

    [RelayCommand]
    private async Task GoToFeedEntryAsync() =>
        await Shell.Current.GoToAsync($"FeedEntry?animalId={AnimalId}");

    [RelayCommand]
    private async Task GoToMedicalEntryAsync() =>
        await Shell.Current.GoToAsync($"MedicalEntry?animalId={AnimalId}&animalTag={TagNumber}");

    [RelayCommand]
    private async Task GoToVaccinationEntryAsync() =>
        await Shell.Current.GoToAsync($"VaccinationEntry?animalId={AnimalId}&animalTag={TagNumber}");

    private class AnimalDetail
    {
        public Guid Id { get; set; }
        public string TagNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string AnimalTypeName { get; set; } = string.Empty;
        public string? BreedName { get; set; }
        public string SexValue { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public decimal? LatestWeightKg { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Notes { get; set; }
    }

    private class ImageUploadResult { public Guid Id { get; set; } }
}
