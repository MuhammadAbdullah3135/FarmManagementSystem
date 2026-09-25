using FMS.Application.Import;

namespace FMS.Application.Finance.Import;

/// <summary>
/// The expense importer's field vocabulary.
///
/// Three fields resolve against the farm — the category, the payment method and an
/// optional location — because a spreadsheet cannot know their ids; an optional animal
/// tag links the expense for cost traceability, exactly as the create endpoint allows.
/// </summary>
public static class ExpenseImportFields
{
    public const string ExpenseDate = "expenseDate";
    public const string Amount = "amount";
    public const string ExpenseCategory = "expenseCategory";
    public const string PaymentMethod = "paymentMethod";
    public const string AnimalTag = "animalTag";
    public const string Location = "location";
    public const string Description = "description";

    /// <summary>Fields whose values must match something the farm already has.</summary>
    public static readonly IReadOnlyList<string> LookupFields = new[]
    {
        ExpenseCategory, PaymentMethod, Location
    };

    /// <summary>Fields carrying a date, parsed with the ambiguous-format guard.</summary>
    public static readonly IReadOnlyList<string> DateFields = new[] { ExpenseDate };

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(ExpenseDate, "Expense date", false,
            "yyyy-MM-dd, or a real Excel date cell. Blank means today"),
        new ImportFieldDescriptor(Amount, "Amount", true,
            "Greater than zero, e.g. 1500.50"),
        new ImportFieldDescriptor(ExpenseCategory, "Expense category", true,
            "Must match an expense category in this farm"),
        new ImportFieldDescriptor(PaymentMethod, "Payment method", true,
            "Must match a payment method in this farm"),
        new ImportFieldDescriptor(AnimalTag, "Animal tag", false,
            "Optional: tag of an animal in this farm, for cost traceability"),
        new ImportFieldDescriptor(Location, "Location", false,
            "Must match a location in this farm"),
        new ImportFieldDescriptor(Description, "Description", false,
            "Free text, e.g. Feed purchase"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no mapping.
    /// Matching is done on the normalized header, so spacing, case and punctuation are
    /// irrelevant.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [ExpenseDate] = new[] { "date", "expense date", "date paid", "paid on", "transaction date" },
        [Amount] = new[] { "amount", "total", "cost", "value", "expense", "amount paid" },
        [ExpenseCategory] = new[] { "category", "expense category", "expense type", "type", "account" },
        [PaymentMethod] = new[] { "payment method", "payment", "paid by", "method", "payment type" },
        [AnimalTag] = new[] { "animal", "animal tag", "tag", "tag number", "animal id" },
        [Location] = new[] { "location", "pen", "shed", "barn", "paddock" },
        [Description] = new[] { "description", "details", "note", "notes", "particulars", "memo" },
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
