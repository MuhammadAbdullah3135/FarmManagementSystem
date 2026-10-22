using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// A farm-maintained veterinary contact. The name is unique per farm without regard
/// to case, and the optional licence number is unique per farm when present; both
/// indexes are functional (on lower(...)), because EF Core's model cannot express an
/// expression index. Clinical records keep their historical <c>VetName</c> string and
/// gain an optional <c>VetId</c> link during the transition away from free text.
/// </summary>
public class Vet : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
}
