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

    /// <summary>
    /// Signs the caller into the shared demo user without credentials. Only available when
    /// <c>DEMO_MODE_ENABLED=true</c>. The demo user is a single flagged account whose farm is
    /// seeded with realistic sample data by <c>DemoDataSeeder</c>.
    /// </summary>
    Task<Result<DemoLoginResponse>> DemoLoginAsync();

    /// <summary>
    /// Restores the demo user, its account, its farm, and all seeded demo data to the original
    /// seed state. Idempotent. Does nothing when no demo user exists. Safe to call from an admin
    /// route, a scheduled job, or <c>dotnet run --project src/FMS.API -- demo:reset</c>.
    /// </summary>
    Task<Result> ResetDemoDataAsync();
}
