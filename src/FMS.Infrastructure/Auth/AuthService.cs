using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Entities;
using FMS.Infrastructure.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly FmsDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        FmsDbContext context,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IEmailService emailService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _emailService = emailService;
        _logger = logger;
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

        // Create the user's initial farm so registration can proceed directly to farm selection.
        var farm = new Domain.Entities.Farm
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Name = request.AccountName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Farms.Add(farm);
        _context.UserFarms.Add(new UserFarm
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FarmId = farm.Id,
            Role = FarmRoles.SystemOwner,
            CreatedAt = DateTime.UtcNow
        });

        // Seed the full default lookup set required by the initial farm.
        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(_context, farm.Id);

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
            LastName = user.LastName,
            Roles = roles
        });
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .Include(u => u.Account)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Unauthorized("Invalid email or password");

        // Preserve the registration invariant for accounts created before the initial-farm fix.
        await EnsureUserHasFarmAsync(user);

        // Backfill default lookup data (sex options, age categories, locations,
        // animal types, breeds) for farms created before the shared seeder
        // existed. Idempotent per category; never allowed to fail a login.
        await BackfillFarmDefaultsAsync(user);

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
            LastName = user.LastName,
            Roles = roles
        });
    }

    private async Task EnsureUserHasFarmAsync(User user)
    {
        var hasActiveFarm = await _context.UserFarms
            .Include(uf => uf.Farm)
            .AnyAsync(uf => uf.UserId == user.Id && uf.Farm.IsActive && !uf.Farm.IsDeleted);

        if (hasActiveFarm)
            return;

        var farm = new Domain.Entities.Farm
        {
            Id = Guid.NewGuid(),
            AccountId = user.AccountId,
            Name = string.IsNullOrWhiteSpace(user.Account.Name) ? "My Farm" : user.Account.Name,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id
        };
        _context.Farms.Add(farm);
        _context.UserFarms.Add(new UserFarm
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FarmId = farm.Id,
            Role = FarmRoles.SystemOwner,
            CreatedAt = DateTime.UtcNow
        });

        // Seed the full default lookup set required by the new farm.
        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(_context, farm.Id);

        await _context.SaveChangesAsync();
    }

    private async Task BackfillFarmDefaultsAsync(User user)
    {
        var farmIds = await _context.UserFarms
            .Include(uf => uf.Farm)
            .Where(uf => uf.UserId == user.Id && uf.Farm.IsActive && !uf.Farm.IsDeleted)
            .Select(uf => uf.FarmId)
            .ToListAsync();

        foreach (var farmId in farmIds)
        {
            try
            {
                await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(_context, farmId);
            }
            catch (Exception ex)
            {
                // Backfilling must never block a login; the next login retries.
                _logger.LogError(ex, "Failed to backfill farm defaults for farm {FarmId}", farmId);
            }
        }
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
            LastName = user.LastName,
            Roles = roles
        });
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);
        if (user == null)
        {
            // Return success even for non-existent emails to prevent user enumeration.
            return Result.Success();
        }

        var rawToken = _jwtTokenService.GenerateRefreshToken();
        var tokenHash = HashToken(rawToken);

        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var resetLink = $"{frontendBaseUrl}/confirm-reset-password?token={rawToken}";

        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink);
        }
        catch (Exception ex)
        {
            // Same asymmetry the notification dispatcher uses for the alert digest: the
            // durable part (here the reset token, already persisted above) survives, and
            // a delivery failure does not fail the request. Failing it would be worse
            // than it looks - the send only happens for addresses that exist, so a 500
            // here would also tell an attacker which accounts are real. The user can
            // simply ask again.
            _logger.LogError(ex,
                "Could not send the password reset email for user {UserId}. The reset token is "
                + "already stored, so a retry will work.",
                user.Id);
        }

        return Result.Success();
    }

    public async Task<Result> ConfirmResetPasswordAsync(ConfirmResetPasswordRequest request)
    {
        var tokenHash = HashToken(request.Token);

        var resetToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(prt => prt.TokenHash == tokenHash);

        if (resetToken == null)
            return Result.Validation("Invalid or expired reset token");

        if (resetToken.IsUsed)
            return Result.Validation("Reset token has already been used");

        if (resetToken.ExpiresAt < DateTime.UtcNow)
            return Result.Validation("Reset token has expired");

        var user = await _context.Users.FindAsync(resetToken.UserId);
        if (user == null)
            return Result.Validation("Invalid or expired reset token");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        resetToken.IsUsed = true;
        resetToken.UsedAt = DateTime.UtcNow;

        // Revoke all existing refresh tokens for this user.
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .ToListAsync();
        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        return Result.Success();
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

    // Delegates to the shared hasher so the reset and invitation flows cannot
    // drift apart.
    private static string HashToken(string token) => TokenHasher.Hash(token);
}
