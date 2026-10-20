using FMS.Application.Common;

namespace FMS.Application.Suggestions;

public interface ISuggestionService
{
    /// <summary>
    /// Returns at most 50 distinct values for one whitelisted field, most recently
    /// used first. Unknown fields are a not-found result rather than an empty list.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetSuggestionsAsync(Guid farmId, string field);
}

/// <summary>
/// The only columns the generic suggestions endpoint may read. Keeping the list
/// closed stops a caller from turning the route into an arbitrary-column reader.
/// </summary>
public static class SuggestionFields
{
    public const string MedicalDiagnosis = "medicalDiagnosis";
    public const string MedicalDosage = "medicalDosage";
    public const string SupplierProductsSupplied = "supplierProductsSupplied";

    public const int Limit = 50;

    public static bool IsKnown(string field) =>
        field is MedicalDiagnosis or MedicalDosage or SupplierProductsSupplied;

    /// <summary>
    /// These fields come from the supplier directory, whose list endpoint is gated
    /// to Employee/FarmManager/SystemOwner. Medical fields follow the medical-record
    /// list endpoint's broader authenticated-member access.
    /// </summary>
    public static bool RequiresSupplierDirectoryRole(string field) =>
        field == SupplierProductsSupplied;
}
