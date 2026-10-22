namespace FMS.Application.Inventory;

/// <summary>
/// The second part of the inventory-category migration: turning the retained
/// <c>InventoryItem.Category</c> strings into category rows and linking the items.
/// It is deliberately not an EF migration, because migrations run automatically when
/// the API starts and this step must be invokable as a dry run first.
/// </summary>
public interface IInventoryCategoryBackfillService
{
    Task<InventoryCategoryBackfillReport> RunAsync(bool apply, CancellationToken cancellationToken = default);
}
