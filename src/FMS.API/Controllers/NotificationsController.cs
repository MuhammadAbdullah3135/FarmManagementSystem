using System.Security.Claims;
using FMS.Application.Common;
using FMS.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// The notification center: the caller's own persisted alerts on the active farm,
/// plus their channel preferences.
///
/// <para>
/// Farm-scoped (<c>{farmId}</c> in the route) and <c>[Authorize]</c> only — any
/// member may read their own notifications, and the service filters every query by
/// the caller's user id, so one member can never see or acknowledge another's.
/// </para>
///
/// <para>
/// Kept farm-scoped rather than account-scoped on purpose: it needs no entry in
/// FarmContextMiddleware's exact-path allowlist, so this feature cannot widen the
/// farm-context enforcement that subphase 3.1 tightened.
/// </para>
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid farmId,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] bool includeDismissed = false,
        [FromQuery] bool includeResolved = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _notificationService.GetAsync(farmId, GetUserId(), new NotificationQuery
        {
            UnreadOnly = unreadOnly,
            IncludeDismissed = includeDismissed,
            IncludeResolved = includeResolved,
            Page = page,
            PageSize = pageSize
        });

        return MapResult(result);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(Guid farmId)
    {
        var result = await _notificationService.GetUnreadCountAsync(farmId, GetUserId());
        return MapResult(result);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid farmId, Guid id)
    {
        var result = await _notificationService.MarkReadAsync(farmId, GetUserId(), id);
        return MapResult(result);
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(Guid farmId)
    {
        var result = await _notificationService.MarkAllReadAsync(farmId, GetUserId());
        return MapResult(result);
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid farmId, Guid id)
    {
        var result = await _notificationService.DismissAsync(farmId, GetUserId(), id);
        return MapResult(result);
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(Guid farmId)
    {
        var result = await _notificationService.GetPreferencesAsync(farmId, GetUserId());
        return MapResult(result);
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        Guid farmId,
        [FromBody] UpdateNotificationPreferencesRequest request)
    {
        var result = await _notificationService.UpdatePreferencesAsync(farmId, GetUserId(), request);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
