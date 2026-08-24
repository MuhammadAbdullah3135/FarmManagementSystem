using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FMS.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly FmsDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;

    public AuthService(
        FmsDbContext context,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return Result<AuthResponse>.Conflict("Email already registered");

        // Create account
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = request.AccountName,
            CreatedAt = DateTime.UtcNow
        };
        _context.Accounts.Add(account);

        // Create user
        var user = new User
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        // Assign default "SystemOwner" role
        var systemOwnerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "SystemOwner");
        if (systemOwnerRole != null)
        {
            _context.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = systemOwnerRole.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var roles = new List<string> { "SystemOwner" };
        var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, account.Id, user.Email, roles);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        // Store refresh token
        _context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_configuration["Jwt:RefreshTokenExpiryDays"]!)),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = user.Id,
            AccountId = account.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName
        });
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Unauthorized("Invalid email or password");

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.AccountId, user.Email, roles);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        // Store refresh token
        _context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_configuration["Jwt:RefreshTokenExpiryDays"]!)),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = user.Id,
            AccountId = user.AccountId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName
        });
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && !rt.IsRevoked);

        if (storedToken == null || storedToken.ExpiresAt < DateTime.UtcNow)
            return Result<AuthResponse>.Unauthorized("Invalid or expired refresh token");

        var user = storedToken.User;
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

        // Revoke old refresh token
        storedToken.IsRevoked = true;
        await _context.SaveChangesAsync();

        // Generate new tokens
        var newAccessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.AccountId, user.Email, roles);
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_configuration["Jwt:RefreshTokenExpiryDays"]!)),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            UserId = user.Id,
            AccountId = user.AccountId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName
        });
    }

    public async Task<Result<string>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);
        if (user == null)
            return Result<string>.NotFound("User not found");

        // Generate reset token (in production, send via email)
        var resetToken = _jwtTokenService.GenerateRefreshToken();

        return Result<string>.Success(resetToken);
    }

    public async Task<Result> RevokeTokenAsync(string refreshToken)
    {
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked);

        if (storedToken == null)
            return Result.NotFound("Token not found");

        storedToken.IsRevoked = true;
        await _context.SaveChangesAsync();

        return Result.Success();
    }
}
