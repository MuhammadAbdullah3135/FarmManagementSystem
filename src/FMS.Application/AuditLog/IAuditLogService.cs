using FMS.Application.Common;

namespace FMS.Application.AuditLog;

public interface IAuditLogService
{
    Task<Result<PagedResult<AuditLogDto>>> GetAuditLogsAsync(Guid farmId, AuditLogFilter filter);
}
