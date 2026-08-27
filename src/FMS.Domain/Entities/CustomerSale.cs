using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class CustomerSale : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime SaleDate { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? IncomeRecordId { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public InventoryItem InventoryItem { get; set; } = null!;
    public StockMovement? StockMovement { get; set; }
    public IncomeRecord? IncomeRecord { get; set; }
}
