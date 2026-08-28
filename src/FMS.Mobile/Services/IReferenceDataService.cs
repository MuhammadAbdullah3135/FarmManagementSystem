using FMS.Mobile.Models;

namespace FMS.Mobile.Services;

/// <summary>
/// Caches reference data (animal types, breeds, feed types, vaccine types, etc.)
/// from the API into SQLite for offline dropdown usage.
/// </summary>
public interface IReferenceDataService
{
    Task<List<CachedReferenceData>> GetAnimalTypesAsync(Guid farmId);
    Task<List<CachedReferenceData>> GetBreedsAsync(Guid farmId);
    Task<List<CachedReferenceData>> GetFeedTypesAsync(Guid farmId);
    Task<List<CachedReferenceData>> GetVaccineTypesAsync(Guid farmId);
    Task<List<CachedReferenceData>> GetAnimalStatusesAsync(Guid farmId);
    Task<List<CachedReferenceData>> GetLocationsAsync(Guid farmId);
    Task SyncAllReferenceDataAsync(Guid farmId);
}
