using System.Security.Claims;
using FMS.Application.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.API.Middleware;

public class FarmContextMiddleware
{
    private readonly RequestDelegate _next;

    public FarmContextMiddleware(RequestDelegate next)
    {
        _next = next;
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

        if (context.Request.Headers.TryGetValue("X-Farm-Id", out var farmIdHeader))
        {
            if (Guid.TryParse(farmIdHeader.ToString(), out var farmId))
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    var hasAccess = await dbContext.UserFarms
                        .AnyAsync(uf => uf.FarmId == farmId && uf.UserId == userId);

                    if (hasAccess)
                    {
                        farmContext.SetCurrentFarmId(farmId);
                    }
                    else
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new { error = "Access denied to this farm" });
                        return;
                    }
                }
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid farm ID format" });
                return;
            }
        }

        await _next(context);
    }
}
