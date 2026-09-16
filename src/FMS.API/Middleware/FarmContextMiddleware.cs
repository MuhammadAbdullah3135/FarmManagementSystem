using System.Security.Claims;
using FMS.Application.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.API.Middleware;

public class FarmContextMiddleware
{
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
            if (userId.HasValue)
            {
                var hasAccess = await dbContext.UserFarms
                    .AnyAsync(uf => uf.FarmId == headerFarmId.Value && uf.UserId == userId.Value);

                if (!hasAccess)
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

            await _next(context);
            return;
        }

        // Case 2: no header — farm-scoped routes must still be membership-checked;
        // otherwise any authenticated user could target any farm by URL alone.
        if (routeFarmId.HasValue && userId.HasValue)
        {
            var hasAccess = await dbContext.UserFarms
                .AnyAsync(uf => uf.FarmId == routeFarmId.Value && uf.UserId == userId.Value);

            if (!hasAccess)
            {
                await DenyFarmAccessAsync(context, userId, routeFarmId.Value, "not a member of the route farm");
                return;
            }

            farmContext.SetCurrentFarmId(routeFarmId.Value);
        }

        await _next(context);
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
