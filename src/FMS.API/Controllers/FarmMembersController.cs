using System.Security.Claims;
using FMS.Application.Common;
using FMS.Application.Farm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Farm membership administration: listing members, changing a member's
/// farm-scoped role, removing a member, and managing invitations.
///
/// All routes are farm-scoped (<c>/api/farm/{farmId}/…</c>) so
/// <see cref="FMS.API.Middleware.FarmContextMiddleware"/> validates that the
/// caller is a member of the farm before these actions run. Whether the caller
/// may *administer* the farm is enforced by the service from their farm role.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}")]
[Authorize]
public class FarmMembersController : ControllerBase
{
    private readonly IFarmMembershipService _membership;

    public FarmMembersController(IFarmMembershipService membership)
    {
        _membership = membership;
    }

    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // ── Members ─────────────────────────────────────────────

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers(Guid farmId)
    {
        var result = await _membership.GetMembersAsync(farmId, UserId);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpPut("members/{userId:guid}/role")]
    public async Task<IActionResult> UpdateMemberRole(
        Guid farmId, Guid userId, [FromBody] UpdateMemberRoleRequest request)
    {
        var result = await _membership.UpdateMemberRoleAsync(farmId, userId, UserId, request);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid farmId, Guid userId)
    {
        var result = await _membership.RemoveMemberAsync(farmId, userId, UserId);
        return result.IsSuccess ? NoContent() : MapError(result.Error);
    }

    // ── Invitations ─────────────────────────────────────────

    [HttpPost("invitations")]
    public async Task<IActionResult> Invite(Guid farmId, [FromBody] InviteMemberRequest request)
    {
        var result = await _membership.InviteAsync(farmId, UserId, request);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> GetInvitations(Guid farmId)
    {
        var result = await _membership.GetPendingInvitationsAsync(farmId, UserId);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid farmId, Guid id)
    {
        var result = await _membership.RevokeInvitationAsync(farmId, id, UserId);
        return result.IsSuccess ? NoContent() : MapError(result.Error);
    }

    private IActionResult MapError(Error? error) =>
        error?.Code switch
        {
            "NotFound" => NotFound(error.Message),
            "Validation" => BadRequest(error.Message),
            "Conflict" => Conflict(error.Message),
            // Forbid() deliberately carries no body: the client's mid-session
            // revocation handling keys off the farm-context denial body and must
            // never react to a role-based 403.
            "Unauthorized" => Forbid(),
            _ => StatusCode(500, error?.Message)
        };
}
