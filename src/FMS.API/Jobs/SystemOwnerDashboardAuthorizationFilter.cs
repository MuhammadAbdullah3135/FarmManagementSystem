using System.Net;
using FMS.Application.Farm;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;

namespace FMS.API.Jobs;

/// <summary>
/// Gates the Hangfire dashboard to SystemOwner.
///
/// The dashboard is only mounted in Development (see <c>Program.cs</c>): a
/// browser navigation to it cannot carry the Bearer token the SPA uses, so
/// gating it in production would require a token/cookie handshake that does not
/// exist. In Development a loopback request is also allowed, which makes the
/// dashboard usable locally without weakening anything reachable off-box.
/// </summary>
public class SystemOwnerDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly IWebHostEnvironment _environment;

    public SystemOwnerDashboardAuthorizationFilter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public bool Authorize(DashboardContext context)
    {
        if (context is not AspNetCoreDashboardContext aspNetCoreContext)
            return false;

        var httpContext = aspNetCoreContext.HttpContext;

        if (httpContext.User.Identity?.IsAuthenticated == true &&
            httpContext.User.IsInRole(FarmRoles.SystemOwner))
        {
            return true;
        }

        return _environment.IsDevelopment() && IsLoopback(httpContext.Connection.RemoteIpAddress);
    }

    private static bool IsLoopback(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(address);
}
