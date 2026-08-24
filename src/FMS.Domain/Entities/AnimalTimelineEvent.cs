using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalTimelineEvent : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public DateTime OccurredAt { get; set; }

    public Animal Animal { get; set; } = null!;
}
