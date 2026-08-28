using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class AnimalListViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;

    [ObservableProperty] private string _searchQuery = string.Empty;

    public ObservableCollection<AnimalListItem> Animals { get; } = new();

    public AnimalListViewModel(IApiService api, IAuthService auth, IDatabaseService db,
        IConnectivityService connectivity, ISyncService sync, IReferenceDataService refData)
    {
        _api = api;
        _auth = auth;
        _db = db;
        Title = "Animals";
        InitializeSyncServices(connectivity, db, auth, sync);
        InitializeSyncCommands(sync, refData);
    }

    [RelayCommand]
    private async Task LoadAnimalsAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        _api.SetFarmHeader(farmId.Value);
        await RunBusyAsync(async () =>
        {
            Animals.Clear();
            try
            {
                var endpoint = $"/farm/{farmId}/animals?pageSize=100";
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                    endpoint += $"&search={Uri.EscapeDataString(SearchQuery)}";

                var result = await _api.GetAsync<AnimalListResult>(endpoint);
                if (result?.Items is not null)
                {
                    foreach (var animal in result.Items)
                        Animals.Add(animal);
                }
            }
            catch
            {
                // Offline fallback: try cache
                var cached = await _db.GetCachedAnimalsAsync(farmId.Value, SearchQuery);
                foreach (var a in cached)
                {
                    Animals.Add(new AnimalListItem
                    {
                        Id = a.Id,
                        TagNumber = a.TagNumber,
                        Name = a.Name,
                        AnimalTypeName = a.AnimalTypeName,
                        BreedName = a.BreedName,
                        StatusName = a.StatusName,
                        LatestWeightKg = a.LatestWeightKg
                    });
                }
            }
        });
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadAnimalsAsync();
    }

    [RelayCommand]
    private async Task ScanTagAsync()
    {
        var scanner = new Views.ScannerPage();
        scanner.TagScanned += async (_, tag) =>
        {
            // Search for animal by scanned tag
            var farmId = _auth.ActiveFarmId;
            if (!farmId.HasValue) return;

            var animal = await _db.GetCachedAnimalByTagAsync(farmId.Value, tag);
            if (animal is not null)
            {
                await Shell.Current.GoToAsync($"AnimalDetail?id={animal.Id}");
            }
            else
            {
                // Try online search
                try
                {
                    _api.SetFarmHeader(farmId.Value);
                    var result = await _api.GetAsync<AnimalListResult>($"/farm/{farmId}/animals?search={Uri.EscapeDataString(tag)}&pageSize=5");
                    if (result?.Items.Count == 1)
                        await Shell.Current.GoToAsync($"AnimalDetail?id={result.Items[0].Id}");
                    else if (result?.Items.Count > 1)
                    {
                        SearchQuery = tag;
                        await LoadAnimalsAsync();
                    }
                    else
                        await Shell.Current.DisplayAlert("Not Found", $"No animal found with tag '{tag}'", "OK");
                }
                catch
                {
                    await Shell.Current.DisplayAlert("Not Found", $"No animal found with tag '{tag}' (offline)", "OK");
                }
            }
        };
        await Shell.Current.Navigation.PushAsync(scanner);
    }

    [RelayCommand]
    private async Task GoToDetailAsync(AnimalListItem animal)
    {
        await Shell.Current.GoToAsync($"AnimalDetail?id={animal.Id}");
    }

    [RelayCommand]
    private async Task GoToCreateAsync()
    {
        await Shell.Current.GoToAsync("AnimalCreate");
    }

    [RelayCommand]
    private async Task SyncNowAsync() => await ExecuteSyncNowAsync();

    [RelayCommand]
    private async Task RetryFailedAsync() => await ExecuteRetryFailedAsync();

    public class AnimalListResult
    {
        public List<AnimalListItem> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class AnimalListItem
    {
        public Guid Id { get; set; }
        public string TagNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string AnimalTypeName { get; set; } = string.Empty;
        public string? BreedName { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public decimal? LatestWeightKg { get; set; }
    }
}
