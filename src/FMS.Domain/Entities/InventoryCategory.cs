using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// A farm-maintained inventory category. The name is unique per farm without regard
/// to case; the database index that enforces that is functional (on lower("Name")),
/// because EF Core's model cannot express an expression index.
/// </summary>
public class InventoryCategory : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Farm Farm { get; set; } = null!;
    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();
}
