using FMS.Application.Common;

namespace FMS.Application.Farm;

/// <summary>
/// Farm membership: who belongs to a farm, with which farm-scoped role, and the
/// invitation flow that adds them.
///
/// Kept deliberately separate from authentication: identity (account, user,
/// password, tokens) lives in <c>IAuthService</c>; this service only ever deals
/// with a user's relationship to a farm.
/// </summary>
public interface IFarmMembershipService
{
    Task<Result<List<FarmMemberDto>>> GetMembersAsync(Guid farmId, Guid requestingUserId);

    Task<Result<FarmInvitationDto>> InviteAsync(Guid farmId, Guid invitedByUserId, InviteMemberRequest request);

    Task<Result<List<FarmInvitationDto>>> GetPendingInvitationsAsync(Guid farmId, Guid requestingUserId);

    Task<Result> RevokeInvitationAsync(Guid farmId, Guid invitationId, Guid requestingUserId);

    Task<Result<FarmMemberDto>> UpdateMemberRoleAsync(Guid farmId, Guid memberUserId, Guid requestingUserId, UpdateMemberRoleRequest request);

    Task<Result> RemoveMemberAsync(Guid farmId, Guid memberUserId, Guid requestingUserId);

    /// <summary>Invitations addressed to the signed-in user's email.</summary>
    Task<Result<List<PendingInvitationDto>>> GetPendingForUserAsync(Guid userId);

    Task<Result<FarmMemberDto>> AcceptAsync(Guid userId, string token);

    Task<Result> DeclineAsync(Guid userId, string token);
}
