using FMS.Domain.Enums;

namespace FMS.Application.AuditLog;

// Filter
public class AuditLogFilter
{
    public string? EntityType { get; set; }
    public Guid? UserId { get; set; }
    public AuditAction? Action { get; set; }
    public string? EntityId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// DTO
public class AuditLogDto
{
    public Guid Id { get; set; }
    public Guid? FarmId { get; set; }
    public string? UserEmail { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? ActionDisplay => Action switch
    {
        AuditAction.Create => "Created",
        AuditAction.Update => "Updated",
        AuditAction.Delete => "Deleted",
        _ => Action.ToString()
    };
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}
