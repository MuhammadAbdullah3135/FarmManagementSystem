namespace FMS.Application.Animal;

public class AnimalTimelineEventDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public DateTime OccurredAt { get; set; }
}
