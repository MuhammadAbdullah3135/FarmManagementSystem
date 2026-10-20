using FMS.Application.Common;
using FMS.Application.Suggestions;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Suggestions;

public class SuggestionService : ISuggestionService
{
    private readonly FmsDbContext _context;

    public SuggestionService(FmsDbContext context) => _context = context;

    public Task<Result<IReadOnlyList<string>>> GetSuggestionsAsync(Guid farmId, string field) =>
        field switch
        {
            SuggestionFields.MedicalDiagnosis => GetMedicalValuesAsync(farmId, SuggestionFields.MedicalDiagnosis),
            SuggestionFields.MedicalDosage => GetMedicalValuesAsync(farmId, SuggestionFields.MedicalDosage),
            SuggestionFields.SupplierProductsSupplied => GetSupplierProductsAsync(farmId),
            _ => Task.FromResult(Result<IReadOnlyList<string>>.NotFound(
                "Unknown suggestion field", DomainMessageKeys.UnknownSuggestionField))
        };

    private async Task<Result<IReadOnlyList<string>>> GetMedicalValuesAsync(Guid farmId, string field)
    {
        // Both projections stay server-side: the grouping and Take(50) run in the
        // database, and only the final label strings cross the wire.
        var values = field == SuggestionFields.MedicalDosage
            ? await _context.MedicalRecords
                .AsNoTracking()
                .Where(record => record.FarmId == farmId && !record.IsDeleted &&
                    record.Dosage != null && record.Dosage != "")
                .GroupBy(record => record.Dosage!.Trim())
                .Select(group => new
                {
                    Value = group.Key,
                    LastUsedAt = group.Max(record => record.DateRecorded)
                })
                .OrderByDescending(row => row.LastUsedAt)
                .ThenBy(row => row.Value)
                .Take(SuggestionFields.Limit)
                .Select(row => row.Value)
                .ToListAsync()
            : await _context.MedicalRecords
                .AsNoTracking()
                .Where(record => record.FarmId == farmId && !record.IsDeleted &&
                    record.Diagnosis != null && record.Diagnosis != "")
                .GroupBy(record => record.Diagnosis!.Trim())
                .Select(group => new
                {
                    Value = group.Key,
                    LastUsedAt = group.Max(record => record.DateRecorded)
                })
                .OrderByDescending(row => row.LastUsedAt)
                .ThenBy(row => row.Value)
                .Take(SuggestionFields.Limit)
                .Select(row => row.Value)
                .ToListAsync();

        return Result<IReadOnlyList<string>>.Success(values);
    }

    private async Task<Result<IReadOnlyList<string>>> GetSupplierProductsAsync(Guid farmId)
    {
        var values = await _context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.FarmId == farmId &&
                supplier.ProductsSupplied != null && supplier.ProductsSupplied != "")
            .GroupBy(supplier => supplier.ProductsSupplied!.Trim())
            .Select(group => new
            {
                Value = group.Key,
                LastUsedAt = group.Max(supplier => supplier.ModifiedAt ?? supplier.CreatedAt)
            })
            .OrderByDescending(row => row.LastUsedAt)
            .ThenBy(row => row.Value)
            .Take(SuggestionFields.Limit)
            .Select(row => row.Value)
            .ToListAsync();

        return Result<IReadOnlyList<string>>.Success(values);
    }
}
