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
}
