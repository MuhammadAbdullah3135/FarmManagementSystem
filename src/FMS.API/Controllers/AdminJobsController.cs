using FMS.Application.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Internal job-status view for the scheduler.
///
/// Account-scoped on purpose: no <c>{farmId}</c> route value and no X-Farm-Id
/// header means FarmContextMiddleware no-ops, so this keeps the caller's
/// *account* roles (SystemOwner) rather than a farm role — the scheduler is
/// infrastructure, not farm data.
/// </summary>
[ApiController]
[Route("api/admin/jobs")]
[Authorize(Roles = "SystemOwner")]
public class AdminJobsController : ControllerBase
{
    private readonly IJobStatusProvider _jobStatusProvider;

    public AdminJobsController(IJobStatusProvider jobStatusProvider)
    {
        _jobStatusProvider = jobStatusProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _jobStatusProvider.GetStatusAsync(cancellationToken);
        return Ok(status);
    }
}
