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
    private readonly IPushSubscriptionService _pushSubscriptionService;

    public NotificationsController(
        INotificationService notificationService,
        IPushSubscriptionService pushSubscriptionService)
    {
        _notificationService = notificationService;
        _pushSubscriptionService = pushSubscriptionService;
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

    /// <summary>
    /// Whether push is available on this deployment, the key a browser must subscribe
    /// with, and the caller's own registered devices.
    ///
    /// Farm-scoped for the same reason every other route here is: a subscription belongs
    /// to a person, but the only thing that decides which of a farm's alerts reaches a
    /// device is that farm's preferences, and keeping the route under the farm keeps the
    /// farm-context gate covering it.
    /// </summary>
    [HttpGet("push")]
    public async Task<IActionResult> GetPushSettings(Guid farmId)
    {
        var result = await _pushSubscriptionService.GetSettingsAsync(farmId, GetUserId());
        return MapResult(result);
    }

    /// <summary>
    /// Registers or refreshes one of the caller's devices.
    ///
    /// Idempotent on the endpoint, and refused with 400 when this deployment cannot send
    /// push at all — storing a device that would never be used is worse than saying so.
    /// </summary>
    [HttpPost("push")]
    public async Task<IActionResult> RegisterPushSubscription(
        Guid farmId,
        [FromBody] RegisterPushSubscriptionRequest request)
    {
        var result = await _pushSubscriptionService.RegisterAsync(farmId, GetUserId(), request);
        return MapResult(result);
    }

    /// <summary>
    /// Removes one of the caller's devices. Another member's id answers 404, never a
    /// silent success.
    /// </summary>
    [HttpDelete("push/{id:guid}")]
    public async Task<IActionResult> UnregisterPushSubscription(Guid farmId, Guid id)
    {
        var result = await _pushSubscriptionService.UnregisterAsync(farmId, GetUserId(), id);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    /// <summary>A void result — the delete. 204 on success, the error body otherwise.</summary>
    private IActionResult MapResult(Result result) => result.IsSuccess
        ? NoContent()
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
