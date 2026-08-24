using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class CustomFieldDefinition : BaseEntity
{
    public Guid FarmId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public string? Options { get; set; }

    public Farm Farm { get; set; } = null!;
}
