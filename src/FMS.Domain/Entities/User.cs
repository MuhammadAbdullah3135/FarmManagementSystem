using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class User : AuditableEntity
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, this account is the shared demo user. Demo logins authenticate as this
    /// user without credentials and are gated behind <c>DEMO_MODE_ENABLED</c>. Demo sessions
    /// are restricted from irreversible/sensitive actions (real email, real payments, password
    /// change, account deletion, real data export) and the demo farm is restored to seed state
    /// by <c>DemoDataSeeder</c> / the demo reset path.
    /// </summary>
    public bool IsDemo { get; set; }

    /// <summary>
    /// The language this user reads, as a BCP-47 language subtag (<c>en</c>, <c>es</c>).
    /// Null until the user chooses one; the client then treats it as unspecified and keeps
    /// whatever the device already uses. The set of accepted values lives in
    /// <see cref="FMS.Application.Common.SupportedLocales"/>.
    /// </summary>
    public string? Locale { get; set; }

    public Account Account { get; set; } = null!;
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<UserFarm> UserFarms { get; set; } = new List<UserFarm>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
}
