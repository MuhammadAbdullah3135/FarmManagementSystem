using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Supplier : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
    public string? ProductsSupplied { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<SupplierPurchase> Purchases { get; set; } = new List<SupplierPurchase>();
}
