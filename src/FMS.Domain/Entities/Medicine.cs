using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Medicine : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int LowStockThreshold { get; set; } = 10;
    public int ExpiringSoonDays { get; set; } = 30;

    public Farm Farm { get; set; } = null!;
    public ICollection<MedicineStock> StockBatches { get; set; } = new List<MedicineStock>();
    public ICollection<MedicineUsage> Usages { get; set; } = new List<MedicineUsage>();
}
