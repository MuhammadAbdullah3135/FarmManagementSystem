using FMS.Application.Common;

namespace FMS.Application.Auth;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// The same role claims embedded in the access token. The client uses these to
    /// filter the navigation menu; the API remains the enforcement point.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// The language stored for this account, or null when the user has never chosen one.
    /// Returning it at sign-in is what makes the choice follow the person to a new device
    /// rather than living only in that browser's localStorage.
    /// </summary>
    public string? Locale { get; set; }
}

/// <summary>
/// What the client needs to know about the signed-in user outside of a token exchange:
/// the same identity facts <c>/auth/me</c> has always returned, plus the stored language.
/// </summary>
public class UserProfileResponse
{
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();

    /// <summary>Null means "never chose" — see <see cref="AuthResponse.Locale"/>.</summary>
    public string? Locale { get; set; }
}

public class SetLocaleRequest
{
    /// <summary>
    /// A language subtag such as <c>es</c>. Validated against
    /// <see cref="FMS.Application.Common.SupportedLocales"/>; anything else is rejected
    /// rather than stored, so a client bug cannot park an unrenderable value on the account.
    /// </summary>
    public string? Locale { get; set; }
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ConfirmResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
