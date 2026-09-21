using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

/// <summary>
/// An invitation for a person (identified by email) to join a farm with a
/// specific farm-scoped role.
///
/// Mirrors the password-reset token pattern from Phase 1: only the SHA-256 hash
/// of the raw token is persisted, the token is single-use (tracked through
/// <see cref="Status"/>), and it expires. The raw token exists only in the
/// invitation link that is emailed out.
/// </summary>
public class FarmInvitation : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public FarmInvitationStatus Status { get; set; } = FarmInvitationStatus.Pending;
    public Guid InvitedByUserId { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid? RespondedByUserId { get; set; }

    public Farm Farm { get; set; } = null!;
}
