using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FMS.Infrastructure.Farm;

public class FarmMembershipService : IFarmMembershipService
{
    private readonly FmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;

    public FarmMembershipService(
        FmsDbContext context,
        IEmailService emailService,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _context = context;
        _emailService = emailService;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
    }

    // ── Members ─────────────────────────────────────────────

    public async Task<Result<List<FarmMemberDto>>> GetMembersAsync(Guid farmId, Guid requestingUserId)
    {
        var requester = await GetMembershipAsync(farmId, requestingUserId);
        if (requester is null)
            return Result<List<FarmMemberDto>>.NotFound("Farm not found or access denied");

        var members = await _context.UserFarms
            .Include(uf => uf.User)
            .Where(uf => uf.FarmId == farmId)
            .OrderBy(uf => uf.CreatedAt)
            .Select(uf => new FarmMemberDto
            {
                UserId = uf.UserId,
                Email = uf.User.Email,
                FirstName = uf.User.FirstName,
                LastName = uf.User.LastName,
                Role = uf.Role,
                JoinedAt = uf.CreatedAt
            })
            .ToListAsync();

        return Result<List<FarmMemberDto>>.Success(members);
    }

    public async Task<Result<FarmMemberDto>> UpdateMemberRoleAsync(
        Guid farmId, Guid memberUserId, Guid requestingUserId, UpdateMemberRoleRequest request)
    {
        var requester = await GetMembershipAsync(farmId, requestingUserId);
        if (requester is null)
            return Result<FarmMemberDto>.NotFound("Farm not found or access denied");
        if (!FarmRoles.IsFarmAdmin(requester.Role))
            return Result<FarmMemberDto>.Unauthorized("Only farm owners and managers can change member roles");
        if (!FarmRoles.IsValid(request.Role))
            return Result<FarmMemberDto>.Validation($"'{request.Role}' is not a valid farm role");

        var member = await _context.UserFarms
            .Include(uf => uf.User)
            .FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == memberUserId);

        if (member is null)
            return Result<FarmMemberDto>.NotFound("That user is not a member of this farm");

        // Demoting the last owner would leave the farm unadministrable.
        if (member.Role == FarmRoles.SystemOwner && request.Role != FarmRoles.SystemOwner &&
            await IsLastOwnerAsync(farmId, memberUserId))
        {
            return Result<FarmMemberDto>.Conflict("A farm must keep at least one owner");
        }

        member.Role = request.Role;
        member.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Result<FarmMemberDto>.Success(MapMember(member));
    }

    public async Task<Result> RemoveMemberAsync(Guid farmId, Guid memberUserId, Guid requestingUserId)
    {
        var requester = await GetMembershipAsync(farmId, requestingUserId);
        if (requester is null)
            return Result.NotFound("Farm not found or access denied");
        if (!FarmRoles.IsFarmAdmin(requester.Role))
            return Result.Unauthorized("Only farm owners and managers can remove members");

        var member = await _context.UserFarms
            .FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == memberUserId);

        if (member is null)
            return Result.NotFound("That user is not a member of this farm");

        if (member.Role == FarmRoles.SystemOwner && await IsLastOwnerAsync(farmId, memberUserId))
            return Result.Conflict("A farm must keep at least one owner");

        _context.UserFarms.Remove(member);

        // Any outstanding invitation for this person is moot once they are out.
        var pending = await _context.FarmInvitations
            .Where(i => i.FarmId == farmId && i.Status == FarmInvitationStatus.Pending)
            .ToListAsync();

        foreach (var invitation in pending)
        {
            invitation.Status = FarmInvitationStatus.Revoked;
            invitation.RespondedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Result.Success();
    }

    // ── Invitations ─────────────────────────────────────────

    public async Task<Result<FarmInvitationDto>> InviteAsync(
        Guid farmId, Guid invitedByUserId, InviteMemberRequest request)
    {
        var requester = await GetMembershipAsync(farmId, invitedByUserId);
        if (requester is null)
            return Result<FarmInvitationDto>.NotFound("Farm not found or access denied");
        if (!FarmRoles.IsFarmAdmin(requester.Role))
            return Result<FarmInvitationDto>.Unauthorized("Only farm owners and managers can invite members");
        if (!FarmRoles.IsValid(request.Role))
            return Result<FarmInvitationDto>.Validation($"'{request.Role}' is not a valid farm role");

        var normalizedEmail = Normalize(request.Email);
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
            return Result<FarmInvitationDto>.Validation("A valid email address is required");

        var farm = await _context.Farms.FirstOrDefaultAsync(f => f.Id == farmId);
        if (farm is null)
            return Result<FarmInvitationDto>.NotFound("Farm not found or access denied");

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (existingUser is not null)
        {
            var alreadyMember = await _context.UserFarms
                .AnyAsync(uf => uf.FarmId == farmId && uf.UserId == existingUser.Id);
            if (alreadyMember)
                return Result<FarmInvitationDto>.Conflict("That person is already a member of this farm");
        }

        var now = DateTime.UtcNow;
        var alreadyInvited = await _context.FarmInvitations.AnyAsync(i =>
            i.FarmId == farmId &&
            i.Email.ToLower() == normalizedEmail &&
            i.Status == FarmInvitationStatus.Pending &&
            i.ExpiresAt > now);

        if (alreadyInvited)
            return Result<FarmInvitationDto>.Conflict("An invitation for that email is already pending");

        var rawToken = _jwtTokenService.GenerateRefreshToken();
        var invitation = new FarmInvitation
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Email = request.Email.Trim(),
            Role = request.Role,
            TokenHash = TokenHasher.Hash(rawToken),
            ExpiresAt = now.AddDays(InvitationExpiryDays),
            Status = FarmInvitationStatus.Pending,
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            CreatedBy = invitedByUserId
        };

        _context.FarmInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var inviteLink = $"{frontendBaseUrl}/accept-invitation?token={rawToken}";
        await _emailService.SendFarmInvitationEmailAsync(invitation.Email, farm.Name, inviteLink);

        return Result<FarmInvitationDto>.Success(MapInvitation(invitation));
    }

    public async Task<Result<List<FarmInvitationDto>>> GetPendingInvitationsAsync(Guid farmId, Guid requestingUserId)
    {
        var requester = await GetMembershipAsync(farmId, requestingUserId);
        if (requester is null)
            return Result<List<FarmInvitationDto>>.NotFound("Farm not found or access denied");
        if (!FarmRoles.IsFarmAdmin(requester.Role))
            return Result<List<FarmInvitationDto>>.Unauthorized("Only farm owners and managers can view invitations");

        var invitations = await _context.FarmInvitations
            .Where(i => i.FarmId == farmId && i.Status == FarmInvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new FarmInvitationDto
            {
                Id = i.Id,
                Email = i.Email,
                Role = i.Role,
                Status = i.Status.ToString(),
                ExpiresAt = i.ExpiresAt,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return Result<List<FarmInvitationDto>>.Success(invitations);
    }

    public async Task<Result> RevokeInvitationAsync(Guid farmId, Guid invitationId, Guid requestingUserId)
    {
        var requester = await GetMembershipAsync(farmId, requestingUserId);
        if (requester is null)
            return Result.NotFound("Farm not found or access denied");
        if (!FarmRoles.IsFarmAdmin(requester.Role))
            return Result.Unauthorized("Only farm owners and managers can revoke invitations");

        var invitation = await _context.FarmInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.FarmId == farmId);

        if (invitation is null)
            return Result.NotFound("Invitation not found");
        if (invitation.Status != FarmInvitationStatus.Pending)
            return Result.Conflict("Only pending invitations can be revoked");

        invitation.Status = FarmInvitationStatus.Revoked;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.RespondedByUserId = requestingUserId;
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<List<PendingInvitationDto>>> GetPendingForUserAsync(Guid userId)
    {
        // Resolved from the database rather than a token claim so the flow does
        // not depend on which email claim the auth handler happens to map.
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Result<List<PendingInvitationDto>>.Unauthorized("User not found");

        var normalizedEmail = Normalize(user.Email);
        var now = DateTime.UtcNow;

        var invitations = await _context.FarmInvitations
            .Include(i => i.Farm)
            .Where(i => i.Email.ToLower() == normalizedEmail &&
                        i.Status == FarmInvitationStatus.Pending &&
                        i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();

        var inviterIds = invitations.Select(i => i.InvitedByUserId).Distinct().ToList();
        var inviters = await _context.Users
            .Where(u => inviterIds.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim());

        var result = invitations.Select(i => new PendingInvitationDto
        {
            Id = i.Id,
            FarmId = i.FarmId,
            FarmName = i.Farm?.Name ?? string.Empty,
            Role = i.Role,
            InvitedByName = inviters.GetValueOrDefault(i.InvitedByUserId, string.Empty),
            ExpiresAt = i.ExpiresAt
        }).ToList();

        return Result<List<PendingInvitationDto>>.Success(result);
    }

    public async Task<Result<FarmMemberDto>> AcceptAsync(Guid userId, string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Result<FarmMemberDto>.Unauthorized("User not found");

        var (invitation, error) = await ResolveInvitationAsync(user.Email, token);
        if (invitation is null)
            return Result<FarmMemberDto>.Validation(error!);

        var now = DateTime.UtcNow;
        var existing = await GetMembershipAsync(invitation.FarmId, userId);

        if (existing is null)
        {
            existing = new UserFarm
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FarmId = invitation.FarmId,
                Role = FarmRoles.IsValid(invitation.Role) ? invitation.Role : FarmRoles.Viewer,
                CreatedAt = now
            };
            _context.UserFarms.Add(existing);
        }

        invitation.Status = FarmInvitationStatus.Accepted;
        invitation.RespondedAt = now;
        invitation.RespondedByUserId = userId;

        await _context.SaveChangesAsync();

        existing.User = user;
        return Result<FarmMemberDto>.Success(MapMember(existing));
    }

    public async Task<Result> DeclineAsync(Guid userId, string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Result.Unauthorized("User not found");

        var (invitation, error) = await ResolveInvitationAsync(user.Email, token);
        if (invitation is null)
            return Result.Validation(error!);

        invitation.Status = FarmInvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.RespondedByUserId = userId;
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Resolves a token to a usable invitation, or returns a reason it cannot be used.
    /// The email match is what stops a leaked token being redeemed by someone else.
    /// </summary>
    private async Task<(FarmInvitation? Invitation, string? Error)> ResolveInvitationAsync(string email, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "An invitation token is required");

        var tokenHash = TokenHasher.Hash(token);
        var invitation = await _context.FarmInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invitation is null)
            return (null, "Invalid or expired invitation");
        if (invitation.Status != FarmInvitationStatus.Pending)
            return (null, "This invitation has already been used or revoked");
        if (invitation.ExpiresAt < DateTime.UtcNow)
            return (null, "This invitation has expired");

        // Case-insensitive: emails are not case-sensitive in practice.
        if (!string.Equals(invitation.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            return (null, "This invitation was sent to a different email address");

        return (invitation, null);
    }

    private async Task<UserFarm?> GetMembershipAsync(Guid farmId, Guid userId) =>
        await _context.UserFarms.FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == userId);

    private async Task<bool> IsLastOwnerAsync(Guid farmId, Guid userId)
    {
        var ownerCount = await _context.UserFarms
            .CountAsync(uf => uf.FarmId == farmId && uf.Role == FarmRoles.SystemOwner);

        return ownerCount <= 1 &&
               await _context.UserFarms.AnyAsync(uf =>
                   uf.FarmId == farmId && uf.UserId == userId && uf.Role == FarmRoles.SystemOwner);
    }

    private int InvitationExpiryDays =>
        int.TryParse(_configuration["Invitations:ExpiryDays"], out var days) ? days : 7;

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static FarmMemberDto MapMember(UserFarm membership) => new()
    {
        UserId = membership.UserId,
        Email = membership.User?.Email ?? string.Empty,
        FirstName = membership.User?.FirstName ?? string.Empty,
        LastName = membership.User?.LastName ?? string.Empty,
        Role = membership.Role,
        JoinedAt = membership.CreatedAt
    };

    private static FarmInvitationDto MapInvitation(FarmInvitation invitation) => new()
    {
        Id = invitation.Id,
        Email = invitation.Email,
        Role = invitation.Role,
        Status = invitation.Status.ToString(),
        ExpiresAt = invitation.ExpiresAt,
        CreatedAt = invitation.CreatedAt
    };
}
