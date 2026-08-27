namespace FMS.Application.Health;

// Medicine DTOs
public class CreateMedicineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int LowStockThreshold { get; set; } = 10;
    public int ExpiringSoonDays { get; set; } = 30;
}

public class UpdateMedicineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int LowStockThreshold { get; set; } = 10;
    public int ExpiringSoonDays { get; set; } = 30;
}

public class MedicineDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int LowStockThreshold { get; set; }
    public int ExpiringSoonDays { get; set; }
    public int TotalQuantity { get; set; }
    public int BatchCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MedicineListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int LowStockThreshold { get; set; }
    public int TotalQuantity { get; set; }
    public int BatchCount { get; set; }
}

// Stock DTOs
public class AddStockBatchRequest
{
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string? Supplier { get; set; }
    public DateTime? DateReceived { get; set; }
}

public class MedicineStockDto
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost => Quantity * UnitCost;
    public DateTime ExpiryDate { get; set; }
    public string? Supplier { get; set; }
    public DateTime DateReceived { get; set; }
}

// Usage DTOs
public class RecordMedicineUsageRequest
{
    public Guid MedicineId { get; set; }
    public Guid? MedicalRecordId { get; set; }
    public int QuantityUsed { get; set; }
    public DateTime? DateUsed { get; set; }
    public string? Notes { get; set; }
}

public class MedicineUsageDto
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public Guid MedicineStockId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public Guid? MedicalRecordId { get; set; }
    public int QuantityUsed { get; set; }
    public DateTime DateUsed { get; set; }
    public string? Notes { get; set; }
}

// Alert DTOs
public enum MedicineAlertType
{
    LowStock,
    ExpiringSoon,
    Expired
}

public class MedicineAlertDto
{
    public Guid MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public Guid StockId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int CurrentQuantity { get; set; }
    public int LowStockThreshold { get; set; }
    public DateTime ExpiryDate { get; set; }
    public MedicineAlertType AlertType { get; set; }
    public string AlertTypeName { get; set; } = string.Empty;
}
