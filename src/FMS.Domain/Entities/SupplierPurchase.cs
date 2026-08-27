using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class SupplierPurchase : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime PurchaseDate { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? ExpenseId { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public InventoryItem InventoryItem { get; set; } = null!;
    public StockMovement? StockMovement { get; set; }
    public Expense? Expense { get; set; }
}
