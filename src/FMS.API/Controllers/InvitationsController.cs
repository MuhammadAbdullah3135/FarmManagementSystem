using System.Security.Claims;
using FMS.Application.Common;
using FMS.Application.Farm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// The invitee's side of the invitation flow. These routes are deliberately NOT
/// farm-scoped: the invitee is not a member of the farm yet, so farm-context
/// middleware would reject them. The single-use token authorises the action, and
/// the caller's own email must match the invitation's.
/// </summary>
[ApiController]
[Route("api/invitations")]
[Authorize]
public class InvitationsController : ControllerBase
{
    private readonly IFarmMembershipService _membership;

    public InvitationsController(IFarmMembershipService membership)
    {
        _membership = membership;
    }

    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("pending-for-me")]
    public async Task<IActionResult> PendingForMe()
    {
        var result = await _membership.GetPendingForUserAsync(UserId);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] InvitationTokenRequest request)
    {
        var result = await _membership.AcceptAsync(UserId, request.Token);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpPost("decline")]
    public async Task<IActionResult> Decline([FromBody] InvitationTokenRequest request)
    {
        var result = await _membership.DeclineAsync(UserId, request.Token);
        return result.IsSuccess ? Ok() : MapError(result.Error);
    }

    private IActionResult MapError(Error? error) =>
        error?.Code switch
        {
            "NotFound" => NotFound(error.Message),
            "Validation" => BadRequest(error.Message),
            "Conflict" => Conflict(error.Message),
            "Unauthorized" => Forbid(),
            _ => StatusCode(500, error?.Message)
        };
}
