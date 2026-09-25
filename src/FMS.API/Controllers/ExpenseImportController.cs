using FMS.Application.Common;
using FMS.Application.Finance.Import;
using FMS.Application.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.API.Controllers;

/// <summary>
/// Bulk expense import from a CSV or Excel file.
///
/// The same two endpoints over the same pipeline as every other importer. To reach it a
/// caller needs exactly what <see cref="FinanceController"/> requires — any authenticated
/// member of the farm — so this controller adds no new role surface.
///
/// <para>
/// Unlike the inventory, employee, supplier and customer importers, this one has no
/// duplicate rule, because an expense has no identifier (see
/// <c>ExpenseImportService</c>). That is a disclosed limitation, not an oversight.
/// </para>
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/finance/expenses/import")]
[Authorize]
public class ExpenseImportController : ControllerBase
{
    private readonly IExpenseImportService _import;
    private readonly ImportOptions _options;

    public ExpenseImportController(IExpenseImportService import, IOptions<ImportOptions> options)
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
