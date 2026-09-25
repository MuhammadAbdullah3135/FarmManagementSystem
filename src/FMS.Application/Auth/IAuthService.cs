using FMS.Application.Common;

namespace FMS.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request);
    Task<Result> ConfirmResetPasswordAsync(ConfirmResetPasswordRequest request);
    Task<Result> RevokeTokenAsync(string refreshToken);

    /// <summary>
    /// The signed-in user's identity facts plus the language stored for their account.
    /// Used by the client on boot to notice a choice made on another device.
    /// </summary>
    Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId);

    /// <summary>
    /// Remembers <paramref name="locale"/> for the user's account. Rejects a language this
    /// build does not ship (<c>Validation</c>) and an unknown user (<c>NotFound</c>), and
    /// treats null/blank as "clear the choice" rather than as an error.
    /// </summary>
    Task<Result<string?>> SetLocaleAsync(Guid userId, string? locale);
}
