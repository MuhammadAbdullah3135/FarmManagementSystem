using System.Security.Claims;
using FMS.Application.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.API.Middleware;

public class FarmContextMiddleware
{
    /// <summary>
    /// Account-scoped invitation actions that must bypass farm-context
    /// enforcement. Kept as exact paths, never a prefix, so unrelated
    /// farm-scoped routes cannot be exempted by accident.
    /// </summary>
    private static readonly HashSet<string> InvitationActionPaths = new(StringComparer.Ordinal)
    {
        "/api/invitations/accept",
        "/api/invitations/decline",
        "/api/invitations/pending-for-me"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<FarmContextMiddleware> _logger;

    public FarmContextMiddleware(RequestDelegate next, ILogger<FarmContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IFarmContextService farmContext, FmsDbContext dbContext)
    {
        // Skip farm context enforcement for auth endpoints (login, register, reset, refresh).
        // These are pre-authentication; stale X-Farm-Id from a previous session must not
        // cause 403 before the endpoint can even run.
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        // The invitee-side invitation routes are account-scoped: the caller is not
        // a member of the farm yet, so membership enforcement would reject the very
        // request that grants membership. A stale X-Farm-Id must not 403 them either.
        //
        // Matched as an exact-path allowlist rather than a path prefix so this can
        // never widen: every farm-scoped route lives under /api/farm/{farmId}/… and
        // therefore cannot equal any entry below.
        var requestPath = context.Request.Path.Value?.TrimEnd('/');
        if (requestPath is not null && InvitationActionPaths.Contains(requestPath))
        {
            await _next(context);
            return;
        }

        // Farm-scoped routes carry {farmId:guid}; resolve it when present.
        Guid? routeFarmId = null;
        if (context.Request.RouteValues.TryGetValue("farmId", out var routeValue) &&
            Guid.TryParse(routeValue?.ToString(), out var parsedRouteFarmId))
        {
            routeFarmId = parsedRouteFarmId;
        }

        Guid? userId = null;
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        Guid? headerFarmId = null;
        if (context.Request.Headers.TryGetValue("X-Farm-Id", out var farmIdHeader))
        {
            if (!Guid.TryParse(farmIdHeader.ToString(), out var parsedHeaderFarmId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid farm ID format" });
                return;
            }

            headerFarmId = parsedHeaderFarmId;
        }

        // Case 1: header present — it defines the session farm. Validate membership,
        // then reject any route that points at a different farm.
        if (headerFarmId.HasValue)
        {
            string? membershipRole = null;
            if (userId.HasValue)
            {
                membershipRole = await dbContext.UserFarms
                    .Where(uf => uf.FarmId == headerFarmId.Value && uf.UserId == userId.Value)
                    .Select(uf => uf.Role)
                    .FirstOrDefaultAsync();

                if (membershipRole is null)
                {
                    await DenyFarmAccessAsync(context, userId, headerFarmId.Value, "not a member of the header farm");
                    return;
                }
            }

            farmContext.SetCurrentFarmId(headerFarmId.Value);

            if (routeFarmId.HasValue && routeFarmId.Value != headerFarmId.Value)
            {
                // The route must not override the header-validated session farm.
                await DenyFarmAccessAsync(context, userId, routeFarmId.Value, "route farm does not match the header farm");
                return;
            }

            // Membership is validated: the request is authorized as this farm's
            // member. Swap in the farm role for the rest of the pipeline.
            if (membershipRole is not null)
            {
                ApplyFarmRole(context, membershipRole);
            }

            await _next(context);
            return;
        }

        // Case 2: no header — farm-scoped routes must still be membership-checked;
        // otherwise any authenticated user could target any farm by URL alone.
        if (routeFarmId.HasValue && userId.HasValue)
        {
            var membershipRole = await dbContext.UserFarms
                .Where(uf => uf.FarmId == routeFarmId.Value && uf.UserId == userId.Value)
                .Select(uf => uf.Role)
                .FirstOrDefaultAsync();

            if (membershipRole is null)
            {
                await DenyFarmAccessAsync(context, userId, routeFarmId.Value, "not a member of the route farm");
                return;
            }

            farmContext.SetCurrentFarmId(routeFarmId.Value);
            ApplyFarmRole(context, membershipRole);
        }

        await _next(context);
    }

    /// <summary>
    /// Replaces the account-level role claims on the principal with the caller's
    /// role in the resolved farm, so a farm-scoped <c>[Authorize(Roles = "…")]</c>
    /// check enforces the farm role rather than the account role.
    ///
    /// Every other claim (NameIdentifier, accountId, email, …) is preserved
    /// unchanged, and the authentication type is carried across so the rebuilt
    /// identity still reports <see cref="ClaimsIdentity.IsAuthenticated"/> —
    /// omitting it would make the framework treat the request as unauthenticated
    /// and return 401 instead of enforcing the role.
    /// </summary>
    private static void ApplyFarmRole(HttpContext context, string farmRole)
    {
        if (context.User.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var claims = identity.Claims
            .Where(claim => claim.Type != ClaimTypes.Role)
            .Append(new Claim(ClaimTypes.Role, farmRole));

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            identity.AuthenticationType,
            identity.NameClaimType,
            identity.RoleClaimType));
    }

    private async Task DenyFarmAccessAsync(HttpContext context, Guid? userId, Guid farmId, string reason)
    {
        _logger.LogWarning(
            "Farm access denied for user {UserId} to farm {FarmId} ({Reason}) on {Method} {Path}",
            userId, farmId, reason, context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Access denied to this farm" });
    }
}
