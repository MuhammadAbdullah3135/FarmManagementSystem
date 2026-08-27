using FMS.Application.Common;

namespace FMS.Application.Inventory;

public class SupplierDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
    public string? ProductsSupplied { get; set; }
    public int PurchaseCount { get; set; }
    public decimal TotalPurchased { get; set; }
}

public class CreateSupplierRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
    public string? ProductsSupplied { get; set; }
}

public class UpdateSupplierRequest : CreateSupplierRequest { }

public class SupplierPurchaseDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid InventoryItemId { get; set; }
    public string InventoryItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime PurchaseDate { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? ExpenseId { get; set; }
    public string? Notes { get; set; }
}

public class CreateSupplierPurchaseRequest
{
    public Guid SupplierId { get; set; }
    public Guid InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public string? Notes { get; set; }
}

public class SupplierListFilter
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SupplierPurchaseListFilter
{
    public Guid? SupplierId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
