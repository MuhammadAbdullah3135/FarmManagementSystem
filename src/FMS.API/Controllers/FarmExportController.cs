using System.Security.Claims;
using FMS.Application.Common;
using FMS.Application.Farm.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Full-farm data export: ask for one, read its state, download the archive.
///
/// Farm-scoped like every other farm route (X-Farm-Id membership, route/header match
/// enforced by <c>FarmContextMiddleware</c>), and role-gated to SystemOwner and
/// FarmManager — the same farm-admin boundary that governs member management and farm
/// settings. The archive is the single most sensitive read in the application: it is
/// every animal, every pound paid, every employee's salary, in one file.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/export")]
[Authorize(Roles = "SystemOwner,FarmManager")]
public class FarmExportController : ControllerBase
{
    private readonly IFarmExportService _exports;
    private readonly IFileStorageService _files;

    public FarmExportController(IFarmExportService exports, IFileStorageService files)
    {
        _exports = exports;
        _files = files;
    }

    /// <summary>
    /// Queues an export of the farm, or returns one already in flight. Answers with the
    /// record's state, not the archive: the build runs in the background, so this request
    /// is one database round trip regardless of how much history the farm holds.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FarmExportDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RequestExport(Guid farmId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("No user id on the authenticated principal");
        }

        var result = await _exports.RequestAsync(farmId, userId, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Accepted(result.Value);
    }

    /// <summary>
    /// The farm's export state. 200 with a null body when this farm has never been
    /// exported — an ordinary state, not a failed request.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FarmExportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExport(Guid farmId, CancellationToken cancellationToken)
    {
        var result = await _exports.GetLatestAsync(farmId, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Streams the stored archive. On object storage a short-lived presigned URL hands the
    /// bytes straight from the bucket; on local disk they stream through this endpoint.
    /// Either way the file is never publicly addressable, and the path always comes from
    /// this farm's own export row.
    /// </summary>
    [HttpGet("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadExport(Guid farmId, CancellationToken cancellationToken)
    {
        var result = await _exports.OpenArchiveAsync(farmId, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var archive = result.Value!;

        var presigned = await _files.GetPresignedDownloadUrlAsync(archive.StoragePath, archive.FileName);
        if (presigned.IsSuccess)
        {
            return Redirect(presigned.Value!.Url);
        }

        var stream = await _files.OpenReadAsync(archive.StoragePath, cancellationToken);
        return File(stream, archive.ContentType, archive.FileName);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MapError(Error error) => error.Code switch
    {
        Error.UnavailableCode => StatusCode(StatusCodes.Status503ServiceUnavailable, error.Message),
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(StatusCodes.Status500InternalServerError, error.Message)
    };
}
