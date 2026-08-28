using FMS.Application.AuditLog;
using FMS.Application.Common;
using FMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/audit-logs")]
[Authorize(Roles = "SystemOwner,FarmManager,Accountant")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs(
        Guid farmId,
        [FromQuery] string? entityType,
        [FromQuery] Guid? userId,
        [FromQuery] AuditAction? action,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new AuditLogFilter
        {
            EntityType = entityType,
            UserId = userId,
            Action = action,
            EntityId = entityId,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
            Page = page,
            PageSize = pageSize
        };

        var result = await _auditLogService.GetAuditLogsAsync(farmId, filter);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error!);
    }

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
