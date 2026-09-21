namespace FMS.Application.Farm;

// ── Members ─────────────────────────────────────────────

public class FarmMemberDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class UpdateMemberRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

// ── Invitations ─────────────────────────────────────────

public class InviteMemberRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class FarmInvitationDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>An invitation waiting on the signed-in user, shown before they accept.</summary>
public class PendingInvitationDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string InvitedByName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class InvitationTokenRequest
{
    public string Token { get; set; } = string.Empty;
}
