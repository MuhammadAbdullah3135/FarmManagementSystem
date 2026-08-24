using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalDocument : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Description { get; set; }

    public Animal Animal { get; set; } = null!;
}
