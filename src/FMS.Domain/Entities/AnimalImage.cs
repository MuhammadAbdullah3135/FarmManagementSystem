using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalImage : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }

    public Animal Animal { get; set; } = null!;
}
