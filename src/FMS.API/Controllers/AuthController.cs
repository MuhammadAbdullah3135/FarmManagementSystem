using FMS.Application.Auth;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// Signs the caller into the shared demo account without credentials.
    /// Disabled entirely when <c>DEMO_MODE_ENABLED</c> is not <c>true</c> (410 Gone), so a
    /// deployment that never wanted a demo simply has no reachable demo endpoint.
    /// </summary>
    [HttpPost("demo-login")]
    [AllowAnonymous]
    [EnableRateLimiting("demo-login")]
    public async Task<IActionResult> DemoLogin([FromBody] DemoLoginRequest request)
    {
        var result = await _authService.DemoLoginAsync();

        if (result.IsSuccess)
            return Ok(result.Value);

        ApiMessageKeys.Attach(Response, result.Error);
        return result.Error?.Code switch
        {
            "Gone" => StatusCode(410, result.Error.Message),
            "Unauthorized" => Unauthorized(result.Error.Message),
            "Validation" => BadRequest(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    /// <summary>
    /// Restores the demo account, farm, and seeded sample data to the original seed state.
    /// Rate-limited as an admin action; disabled when demo mode is off (410 Gone).
    /// </summary>
    [HttpPost("demo-reset")]
    [Authorize(Roles = "SystemOwner")]
    [EnableRateLimiting("demo-reset")]
    public async Task<IActionResult> DemoReset()
    {
        var result = await _authService.ResetDemoDataAsync();

        if (result.IsSuccess)
            return Ok(new { demoReset = true });

        ApiMessageKeys.Attach(Response, result.Error);
        return result.Error?.Code switch
        {
            "Gone" => StatusCode(410, result.Error.Message),
            "NotFound" => NotFound(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);

        if (result.IsSuccess)
            return Created("", result.Value);

        ApiMessageKeys.Attach(Response, result.Error);
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

        ApiMessageKeys.Attach(Response, result.Error);
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

        ApiMessageKeys.Attach(Response, result.Error);
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

        ApiMessageKeys.Attach(Response, result.Error);
        return result.Error?.Code switch
        {
            // The only way this path fails is the demo-account refusal; a non-demo address
            // always answers 200 (including unknown ones, to avoid enumeration).
            "Validation" => BadRequest(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpPost("confirm-reset-password")]
    public async Task<IActionResult> ConfirmResetPassword([FromBody] ConfirmResetPasswordRequest request)
    {
        var result = await _authService.ConfirmResetPasswordAsync(request);

        if (result.IsSuccess)
            return Ok();

        ApiMessageKeys.Attach(Response, result.Error);
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

        ApiMessageKeys.Attach(Response, result.Error);
        return result.Error?.Code switch
        {
            "NotFound" => NotFound(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // The identity facts are read from the token, exactly as before, so this stays a
        // cheap account-scoped read that cannot disagree with the caller's own claims. The
        // language is the one thing the token does not carry, so it — and only it — comes
        // from the database.
        if (!Guid.TryParse(userId, out var parsed))
            return Unauthorized();

        var profile = await _authService.GetProfileAsync(parsed);
        if (!profile.IsSuccess)
            return MapError(profile.Error!);

        return Ok(new
        {
            userId,
            email = User.FindFirst(ClaimTypes.Email)?.Value,
            accountId = User.FindFirst("accountId")?.Value,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
            locale = profile.Value!.Locale
        });
    }

    /// <summary>
    /// Remembers the language for this account so the choice follows the user to another
    /// device. Device-local storage already applied it; this is what makes it durable.
    /// </summary>
    [Authorize]
    [HttpPut("me/locale")]
    public async Task<IActionResult> SetLocale([FromBody] SetLocaleRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var parsed))
            return Unauthorized();

        var result = await _authService.SetLocaleAsync(parsed, request.Locale);
        if (!result.IsSuccess)
            return MapError(result.Error!);

        return Ok(new { locale = result.Value });
    }

    private IActionResult MapError(Error error)
    {
        ApiMessageKeys.Attach(Response, error);
        return error.Code switch
        {
            "NotFound" => NotFound(error.Message),
            "Validation" => BadRequest(error.Message),
            "Unauthorized" => Unauthorized(error.Message),
            _ => StatusCode(500, error.Message)
        };
    }
}
