using FMS.Application.Import;

namespace FMS.Application.Inventory.Import;

/// <summary>
/// The customer importer's field vocabulary.
///
/// Customer is the shallowest of the importers: a name and a contact, nothing resolved
/// against farm configuration. The only relational rule is the name, which is the
/// customer's identifier (the unique index on (FarmId, Name) enforces it).
/// </summary>
public static class CustomerImportFields
{
    public const string Name = "name";
    public const string ContactInfo = "contactInfo";

    /// <summary>Customer has no farm-configured lookups.</summary>
    public static readonly IReadOnlyList<string> LookupFields = Array.Empty<string>();

    /// <summary>No field on a customer is a date.</summary>
    public static readonly IReadOnlyList<string> DateFields = Array.Empty<string>();

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(Name, "Customer name", true,
            "Unique within the farm: this is the customer's identifier"),
        new ImportFieldDescriptor(ContactInfo, "Contact information", false,
            "Free text, e.g. +254 700 000000"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no mapping.
    /// Matching is done on the normalized header, so spacing, case and punctuation are
    /// irrelevant.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [Name] = new[] { "name", "customer", "customer name", "client", "buyer", "company" },
        [ContactInfo] = new[] { "contact", "contact info", "contact information", "phone", "email", "telephone", "address" },
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
