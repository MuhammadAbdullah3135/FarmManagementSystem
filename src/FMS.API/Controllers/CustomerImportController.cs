using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.API.Controllers;

/// <summary>
/// Bulk customer import from a CSV or Excel file.
///
/// The same two endpoints over the same pipeline as every other importer: <c>preview</c>
/// validates and writes nothing, <c>commit</c> re-reads and re-validates the same bytes
/// and writes the whole batch in one transaction.
///
/// Authorization is exactly the single-customer create path
/// (<see cref="CustomersController"/>), so this controller adds no new role surface.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/inventory/customers/import")]
[Authorize(Roles = "Employee,FarmManager,SystemOwner")]
public class CustomerImportController : ControllerBase
{
    private readonly ICustomerImportService _import;
    private readonly ImportOptions _options;

    public CustomerImportController(ICustomerImportService import, IOptions<ImportOptions> options)
    {
        _import = import;
        _options = options.Value;
    }

    [HttpPost("preview")]
    [RequestSizeLimit(ImportOptions.DefaultMaxFileBytes)]
    public Task<IActionResult> Preview(
        Guid farmId,
        IFormFile file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken) =>
        RunAsync(farmId, file, mapping, commit: false, cancellationToken);

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
