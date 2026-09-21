using FMS.Application.Import;

namespace FMS.Application.Inventory.Import;

/// <summary>
/// The inventory importer's field vocabulary.
///
/// Inventory is the shallowest of the three: nothing here resolves against farm
/// configuration, because a category and a location are free text on an inventory item
/// rather than shared records. The only relational rule is the name, which is the
/// item's identifier — see <c>InventoryItemRules.DuplicateNameMessage</c>.
/// </summary>
public static class InventoryImportFields
{
    public const string Name = "name";
    public const string Category = "category";
    public const string Unit = "unit";
    public const string Quantity = "quantity";
    public const string ReorderLevel = "reorderLevel";
    public const string UnitCost = "unitCost";
    public const string Location = "location";

    /// <summary>
    /// Inventory has no farm-configured lookups, so a fixed value is offered for the
    /// text fields but nothing is validated against a list.
    /// </summary>
    public static readonly IReadOnlyList<string> LookupFields = Array.Empty<string>();

    /// <summary>No field on an inventory item is a date.</summary>
    public static readonly IReadOnlyList<string> DateFields = Array.Empty<string>();

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(Name, "Item name", true,
            "Unique within the farm: this is the item's identifier"),
        new ImportFieldDescriptor(Category, "Category", false,
            "Free text, e.g. Feed"),
        new ImportFieldDescriptor(Unit, "Unit", true,
            "e.g. kg, litre, bag"),
        new ImportFieldDescriptor(Quantity, "Quantity", false,
            "Opening stock. Recorded as a purchase movement, e.g. 250"),
        new ImportFieldDescriptor(ReorderLevel, "Reorder level", false,
            "Low-stock threshold, e.g. 50"),
        new ImportFieldDescriptor(UnitCost, "Unit cost", false,
            "Cost per unit, e.g. 12.50"),
        new ImportFieldDescriptor(Location, "Location", false,
            "Free text, e.g. Store room"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no mapping.
    /// Matching is done on the normalized header, so spacing, case and punctuation are
    /// irrelevant.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [Name] = new[] { "name", "item", "item name", "description", "product", "inventory item", "stock item" },
        [Category] = new[] { "category", "type", "group", "item category", "class" },
        [Unit] = new[] { "unit", "uom", "units", "unit of measure", "measure", "unit of issue" },
        [Quantity] = new[] { "quantity", "qty", "stock", "on hand", "quantity on hand", "opening stock", "balance" },
        [ReorderLevel] = new[] { "reorder level", "reorder", "reorder point", "minimum", "min", "min level", "minimum level" },
        [UnitCost] = new[] { "unit cost", "cost", "price", "unit price", "cost per unit", "rate" },
        [Location] = new[] { "location", "store", "store room", "warehouse", "shelf", "bin", "room" },
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
