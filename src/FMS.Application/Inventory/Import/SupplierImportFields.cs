using FMS.Application.Import;

namespace FMS.Application.Inventory.Import;

/// <summary>
/// The supplier importer's field vocabulary.
///
/// Supplier has nothing that resolves against farm configuration: a contact and the
/// products supplied are free text, and the only relational rule is the name, which is
/// the supplier's identifier (the unique index on (FarmId, Name) enforces it). So this
/// is closer to the inventory vocabulary than to the employee one.
/// </summary>
public static class SupplierImportFields
{
    public const string Name = "name";
    public const string ContactInfo = "contactInfo";
    public const string ProductsSupplied = "productsSupplied";

    /// <summary>Supplier has no farm-configured lookups.</summary>
    public static readonly IReadOnlyList<string> LookupFields = Array.Empty<string>();

    /// <summary>No field on a supplier is a date.</summary>
    public static readonly IReadOnlyList<string> DateFields = Array.Empty<string>();

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(Name, "Supplier name", true,
            "Unique within the farm: this is the supplier's identifier"),
        new ImportFieldDescriptor(ContactInfo, "Contact information", false,
            "Free text, e.g. +254 700 000000"),
        new ImportFieldDescriptor(ProductsSupplied, "Products supplied", false,
            "Free text, e.g. Feed, vet supplies"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no mapping.
    /// Matching is done on the normalized header, so spacing, case and punctuation are
    /// irrelevant.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [Name] = new[] { "name", "supplier", "supplier name", "vendor", "company", "business name" },
        [ContactInfo] = new[] { "contact", "contact info", "contact information", "phone", "email", "telephone", "address" },
        [ProductsSupplied] = new[] { "products", "products supplied", "supplies", "goods", "items supplied" },
    };

    /// <summary>
    /// Declared last on purpose: static fields initialize in textual order, and this one
    /// is built from <see cref="All"/> and <see cref="Aliases"/> above.
    /// </summary>
    private static readonly ImportFieldCatalog Catalog = new(All, Aliases);

    /// <summary>This vocabulary as the shared pipeline consumes it.</summary>
    public static IImportFieldCatalog FieldCatalog => Catalog;

    public static ImportFieldDescriptor? Describe(string key) => Catalog.Describe(key);

    public static string Normalize(string? value) => ImportFieldCatalog.Normalize(value);

    public static string? MatchHeader(string header) => Catalog.MatchHeader(header);
}
