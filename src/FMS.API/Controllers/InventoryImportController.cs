using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.API.Controllers;

/// <summary>
/// Bulk inventory import from a CSV or Excel file.
///
/// Two endpoints over one pipeline: <c>preview</c> validates the file and writes
/// nothing, <c>commit</c> re-reads and re-validates the same bytes and then writes the
/// whole batch in one transaction. The commit deliberately does not accept a
/// server-side handle from the preview — there is no temporary state to expire, and
/// nothing a caller can edit between the two calls can bypass validation.
///
/// Authorization is exactly the single-item create path
/// (<see cref="InventoryItemsController"/>, same roles and same farm scope), so this
/// controller adds no new role surface.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/inventory-items/import")]
[Authorize(Roles = "Employee,Veterinarian,FarmManager,SystemOwner")]
public class InventoryImportController : ControllerBase
{
    private readonly IInventoryImportService _import;
    private readonly ImportOptions _options;

    public InventoryImportController(IInventoryImportService import, IOptions<ImportOptions> options)
    {
        _import = import;
        _options = options.Value;
    }

    /// <summary>
    /// Validates an upload and reports every row, without writing anything. Called with
    /// no mapping it returns the file's headers, the mapping it would choose and the
    /// results under that guess, which is what the wizard's first step needs.
    /// </summary>
    [HttpPost("preview")]
    [RequestSizeLimit(ImportOptions.DefaultMaxFileBytes)]
    public Task<IActionResult> Preview(
        Guid farmId,
        IFormFile file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken) =>
        RunAsync(farmId, file, mapping, commit: false, cancellationToken);

    /// <summary>
    /// Imports the file. All-or-nothing: a single invalid row means nothing is written
    /// and the response carries zero imported rows plus every problem found, so the
    /// caller can correct the file and upload it again.
    /// </summary>
    [HttpPost("commit")]
    [RequestSizeLimit(ImportOptions.DefaultMaxFileBytes)]
    public Task<IActionResult> Commit(
        Guid farmId,
        IFormFile file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken) =>
        RunAsync(farmId, file, mapping, commit: true, cancellationToken);

    private async Task<IActionResult> RunAsync(
        Guid farmId,
        IFormFile? file,
        string? mappingJson,
        bool commit,
        CancellationToken cancellationToken)
    {
        if (ImportUpload.Validate(file, _options) is { } problem)
            return BadRequest(problem);

        if (!ImportUpload.TryDecodeMapping(mappingJson, out var mapping, out var mappingError))
            return BadRequest(mappingError);

        await using var stream = file!.OpenReadStream();

        if (commit)
        {
            var outcome = await _import.CommitAsync(farmId, stream, file.FileName, mapping, cancellationToken);
            return outcome.IsSuccess ? Ok(outcome.Value) : ImportUpload.MapError(outcome.Error!);
        }

        var preview = await _import.PreviewAsync(farmId, stream, file.FileName, mapping, cancellationToken);
        return preview.IsSuccess ? Ok(preview.Value) : ImportUpload.MapError(preview.Error!);
    }
}
