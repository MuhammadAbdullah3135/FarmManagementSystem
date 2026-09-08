using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class MedicineStock : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid MedicineId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string? Supplier { get; set; }
    public DateTime DateReceived { get; set; }

    public Farm Farm { get; set; } = null!;
    public Medicine Medicine { get; set; } = null!;
}
