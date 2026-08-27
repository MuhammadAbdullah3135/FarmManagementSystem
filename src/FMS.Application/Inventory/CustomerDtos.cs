namespace FMS.Application.Inventory;

public class CustomerDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
    public int SaleCount { get; set; }
    public decimal TotalSales { get; set; }
}

public class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
}

public class UpdateCustomerRequest : CreateCustomerRequest { }

public class CustomerSaleDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid InventoryItemId { get; set; }
    public string InventoryItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime SaleDate { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? IncomeRecordId { get; set; }
    public string? Notes { get; set; }
}

public class CreateCustomerSaleRequest
{
    public Guid CustomerId { get; set; }
    public Guid InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? SaleDate { get; set; }
    public Guid? IncomeCategoryId { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public string? Notes { get; set; }
}

public class CustomerListFilter
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CustomerSaleListFilter
{
    public Guid? CustomerId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
