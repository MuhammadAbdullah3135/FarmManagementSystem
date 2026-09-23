using FMS.Application.Common;
using FMS.Application.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Applies work a device did while it was offline.
///
/// <para>
/// Farm-scoped exactly like every other farm route: the <c>{farmId}</c> in the route is what
/// <c>FarmContextMiddleware</c> validates against the caller's membership and the
/// <c>X-Farm-Id</c> header, so a sync runs under the same farm context a live request does —
/// membership is re-checked when the queue arrives, not trusted from when it was filled.
/// </para>
///
/// <para>
/// <c>[Authorize]</c> with no roles, matching the three workflows it applies (animal weights,
/// attendance and task completion all require any farm member). The per-operation requirement
/// lives in <see cref="SyncOperations.RequiredRoles"/> and is enforced by the service, with a
/// test binding the two together.
/// </para>
///
/// <para>
/// The batch always answers <c>200</c> when it is a well-formed request: per-item outcomes are
/// the payload, because a queue with one bad row must still get its other items applied. A
/// malformed <em>request</em> — a body that cannot be bound, or more items than the transport
/// guard allows — is a <c>400</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/sync")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly IMutationSyncService _syncService;

    public SyncController(IMutationSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Applies each item by running the workflow's own service method. Re-sending a batch is
    /// safe: an item whose client mutation id has already been processed is answered with the
    /// result recorded the first time.
    /// </summary>
    [HttpPost("mutations")]
    public async Task<IActionResult> ApplyMutations(
        Guid farmId, [FromBody] SyncMutationRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count > SyncMutationRequest.MaxItemsPerRequest)
        {
            return BadRequest(
                $"A sync request cannot contain more than {SyncMutationRequest.MaxItemsPerRequest} items");
        }

        var result = await _syncService.ApplyAsync(farmId, request, cancellationToken);
        return Ok(result);
    }
}
