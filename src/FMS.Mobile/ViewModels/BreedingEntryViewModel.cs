using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

[QueryProperty(nameof(AutoSelectSireId), "sireId")]
[QueryProperty(nameof(AutoSelectSireTag), "sireTag")]
[QueryProperty(nameof(AutoSelectDamId), "damId")]
[QueryProperty(nameof(AutoSelectDamTag), "damTag")]
public partial class BreedingEntryViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private List<CachedAnimal> _allAnimals = new();

    [ObservableProperty] private Guid? _sireId;
    [ObservableProperty] private string _sireTag = string.Empty;
    [ObservableProperty] private Guid? _damId;
    [ObservableProperty] private string _damTag = string.Empty;
    [ObservableProperty] private DateTime _breedingDate = DateTime.Now;
    [ObservableProperty] private string _selectedMethod = "Natural";
    [ObservableProperty] private string? _vetName;
    [ObservableProperty] private string? _notes;

    // Result is only shown when editing an existing record
    [ObservableProperty] private bool _showResult;
    [ObservableProperty] private string _selectedResult = "Pending";

    public ObservableCollection<string> Methods { get; } = new()
    {
        "Natural", "AI", "ET"
    };

    public ObservableCollection<string> Results { get; } = new()
    {
        "Pending", "Confirmed", "Failed"
    };

    // Filtered animal lists for sire/dam pickers
    public ObservableCollection<AnimalOption> MaleAnimals { get; } = new();
    public ObservableCollection<AnimalOption> FemaleAnimals { get; } = new();

    // Auto-select properties for query navigation
    public Guid? AutoSelectSireId
    {
        get => null;
        set
        {
            if (value.HasValue && value.Value != Guid.Empty)
                SireId = value.Value;
        }
    }

    public string? AutoSelectSireTag
    {
        get => null;
        set
        {
            if (!string.IsNullOrEmpty(value))
                SireTag = value;
        }
    }

    public Guid? AutoSelectDamId
    {
        get => null;
        set
        {
            if (value.HasValue && value.Value != Guid.Empty)
                DamId = value.Value;
        }
    }

    public string? AutoSelectDamTag
    {
        get => null;
        set
        {
            if (!string.IsNullOrEmpty(value))
                DamTag = value;
        }
    }

    public BreedingEntryViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        Title = "Record Breeding";
    }

    [RelayCommand]
    private async Task LoadAnimalsAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        try
        {
            // Try online first
            _api.SetFarmHeader(farmId.Value);
            var result = await _api.GetAsync<List<CachedAnimal>>($"/farm/{farmId}/animals/lookup");
            if (result != null)
                _allAnimals = result;
        }
        catch
        {
            // Fall back to cached animals
            _allAnimals = await _db.GetCachedAnimalsAsync(farmId.Value);
        }

        MaleAnimals.Clear();
        FemaleAnimals.Clear();

        foreach (var a in _allAnimals)
        {
            var option = new AnimalOption { Id = a.Id, TagNumber = a.TagNumber, Name = a.Name };
            var sex = a.SexValue?.ToLowerInvariant() ?? "";
            if (sex.Contains("male") || sex == "m")
                MaleAnimals.Add(option);
            else if (sex.Contains("female") || sex == "f")
                FemaleAnimals.Add(option);
        }

        // If we couldn't determine sex, show all in both lists
        if (MaleAnimals.Count == 0 && FemaleAnimals.Count == 0)
        {
            foreach (var a in _allAnimals)
            {
                var option = new AnimalOption { Id = a.Id, TagNumber = a.TagNumber, Name = a.Name };
                MaleAnimals.Add(option);
                FemaleAnimals.Add(option);
            }
        }
    }

    [RelayCommand]
    private async Task LookupSireAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        // Build action sheet from male animals
        List<AnimalOption> maleList;
        if (MaleAnimals.Count > 0)
        {
            maleList = MaleAnimals.ToList();
        }
        else
        {
            maleList = _allAnimals.Select(a => new AnimalOption { Id = a.Id, TagNumber = a.TagNumber, Name = a.Name }).ToList();
        }

        if (maleList.Count == 0)
        {
            await Shell.Current.DisplayAlert("No Animals", "No cached animals found. Sync your farm first, or type the tag number manually.", "OK");
            return;
        }

        var options = maleList.Select(a => a.DisplayName).ToList();
        options.Add("Cancel");

        var choice = await Shell.Current.DisplayActionSheet("Select Sire (Male)", "Cancel", null, options.ToArray());
        if (choice != null && choice != "Cancel")
        {
            var selected = maleList.FirstOrDefault(a => a.DisplayName == choice);
            if (selected != null)
            {
                SireId = selected.Id;
                SireTag = selected.TagNumber;
            }
        }
    }

    [RelayCommand]
    private async Task LookupDamAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        List<AnimalOption> femaleList;
        if (FemaleAnimals.Count > 0)
        {
            femaleList = FemaleAnimals.ToList();
        }
        else
        {
            femaleList = _allAnimals.Select(a => new AnimalOption { Id = a.Id, TagNumber = a.TagNumber, Name = a.Name }).ToList();
        }

        if (femaleList.Count == 0)
        {
            await Shell.Current.DisplayAlert("No Animals", "No cached animals found. Sync your farm first, or type the tag number manually.", "OK");
            return;
        }

        var options = femaleList.Select(a => a.DisplayName).ToList();
        options.Add("Cancel");

        var choice = await Shell.Current.DisplayActionSheet("Select Dam (Female)", "Cancel", null, options.ToArray());
        if (choice != null && choice != "Cancel")
        {
            var selected = femaleList.FirstOrDefault(a => a.DisplayName == choice);
            if (selected != null)
            {
                DamId = selected.Id;
                DamTag = selected.TagNumber;
            }
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!SireId.HasValue || !DamId.HasValue)
        {
            await Shell.Current.DisplayAlert("Validation", "Both sire and dam are required. Use the lookup button or type tag numbers.", "OK");
            return;
        }

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);

            var method = SelectedMethod switch
            {
                "AI" => 1,
                "ET" => 2,
                _ => 0
            };

            var payload = new
            {
                sireId = SireId.Value,
                damId = DamId.Value,
                breedingDate = BreedingDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                method,
                vetName = VetName,
                notes = Notes
            };

            try
            {
                await _api.PostAsync($"/farm/{farmId}/breeding-records", payload);
                await Shell.Current.DisplayAlert("Saved", "Breeding record saved", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "BreedingRecord",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                });
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlert("Queued", "Breeding record will sync when online", "OK");
                await Shell.Current.GoToAsync("..");
            }
        });
    }
}

/// <summary>
/// Simple animal option for picker display.
/// </summary>
public class AnimalOption
{
    public Guid Id { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string DisplayName => string.IsNullOrEmpty(Name) ? TagNumber : $"{TagNumber} - {Name}";
}
