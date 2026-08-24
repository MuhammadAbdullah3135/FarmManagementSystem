namespace FMS.Domain.Common;

public abstract class AuditableEntity : BaseEntity
{
    public Guid? CreatedBy { get; set; }
    public Guid? ModifiedBy { get; set; }
}
