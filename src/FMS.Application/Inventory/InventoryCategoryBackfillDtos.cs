namespace FMS.Application.Inventory;

/// <summary>
/// One farm's share of an inventory-category backfill. The totals on
/// <see cref="InventoryCategoryBackfillReport"/> are the sums of these rows.
/// </summary>
public class InventoryCategoryBackfillFarmReport
{
    public Guid FarmId { get; set; }
    public int CategoriesCreated { get; set; }
    public int ItemsLinked { get; set; }
    public int BlankCategoryItemsLeftNull { get; set; }
}

/// <summary>
/// What a backfill run did, or — for a dry run — what it would do. Running the
/// same backfill again reports zero categories created and zero items linked,
/// because it only considers items whose <c>CategoryId</c> is still null.
/// </summary>
public class InventoryCategoryBackfillReport
{
    public bool Applied { get; set; }
    public int CategoriesCreated { get; set; }
    public int ItemsLinked { get; set; }
    public int BlankCategoryItemsLeftNull { get; set; }
    public List<InventoryCategoryBackfillFarmReport> Farms { get; set; } = new();
}
