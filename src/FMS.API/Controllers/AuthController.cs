using FMS.Application.Auth;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);

        if (result.IsSuccess)
            return Created("", result.Value);

        return result.Error?.Code switch
        {
            "Conflict" => Conflict(result.Error.Message),
            "Validation" => BadRequest(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Code switch
        {
            "Unauthorized" => Unauthorized(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Code switch
        {
            "Unauthorized" => Unauthorized(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);

        if (result.IsSuccess)
            return Ok();

        return StatusCode(500, result.Error?.Message);
    }

    [HttpPost("confirm-reset-password")]
    public async Task<IActionResult> ConfirmResetPassword([FromBody] ConfirmResetPasswordRequest request)
    {
        var result = await _authService.ConfirmResetPasswordAsync(request);

        if (result.IsSuccess)
            return Ok();

        return result.Error?.Code switch
        {
            "Validation" => BadRequest(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RevokeTokenAsync(request.RefreshToken);

        if (result.IsSuccess)
            return Ok();

        return result.Error?.Code switch
        {
            "NotFound" => NotFound(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var accountId = User.FindFirst("accountId")?.Value;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return Ok(new
        {
            userId,
            email,
            accountId,
            roles
        });
    }
}
