using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Customer : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<CustomerSale> Sales { get; set; } = new List<CustomerSale>();
}
