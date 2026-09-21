using System.Text.Json;
using FMS.Application.Animal.Import;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.API.Controllers;

/// <summary>
/// Bulk animal import from a CSV or Excel file.
///
/// Two endpoints over one pipeline: <c>preview</c> validates the file and writes
/// nothing, <c>commit</c> re-reads and re-validates the same bytes and then writes the
/// whole batch in one transaction. The commit deliberately does not accept a
/// server-side handle from the preview — there is no temporary state to expire, and
/// nothing a caller can edit between the two calls can bypass validation.
///
/// Authorization is exactly the single-animal create path (authenticated member of
/// the addressed farm), so this controller adds no new role surface.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/animals/import")]
[Authorize]
public class AnimalImportController : ControllerBase
{
    private static readonly JsonSerializerOptions MappingJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAnimalImportService _import;
    private readonly AnimalImportOptions _options;

    public AnimalImportController(IAnimalImportService import, IOptions<AnimalImportOptions> options)
    {
        _import = import;
        _options = options.Value;
    }

    /// <summary>
    /// Validates an upload and reports every row, without writing anything. Called
    /// with no mapping it returns the file's headers, the mapping it would choose and
    /// the results under that guess, which is what the wizard's first step needs.
    /// </summary>
    [HttpPost("preview")]
    [RequestSizeLimit(AnimalImportOptions.DefaultMaxFileBytes)]
    public Task<IActionResult> Preview(
        Guid farmId,
        IFormFile file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken) =>
        RunAsync(farmId, file, mapping, commit: false, cancellationToken);

    /// <summary>
    /// Imports the file. All-or-nothing: a single invalid row means nothing is
    /// written and the response carries zero imported rows plus every problem found,
    /// so the caller can correct the file and upload it again.
    /// </summary>
    [HttpPost("commit")]
    [RequestSizeLimit(AnimalImportOptions.DefaultMaxFileBytes)]
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
        if (file is null || file.Length == 0)
            return BadRequest("A file is required.");

        if (file.Length > _options.MaxFileBytes)
            return BadRequest(
                $"The file is {file.Length / (1024.0 * 1024.0):0.#} MB. The limit for one import is " +
                $"{_options.MaxFileBytes / (1024.0 * 1024.0):0.#} MB.");

        AnimalImportMapping? mapping;
        try
        {
            mapping = string.IsNullOrWhiteSpace(mappingJson)
                ? null
                : JsonSerializer.Deserialize<AnimalImportMapping>(mappingJson, MappingJsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest("The column mapping could not be read.");
        }

        await using var stream = file.OpenReadStream();

        if (commit)
        {
            var outcome = await _import.CommitAsync(farmId, stream, file.FileName, mapping, cancellationToken);
            return outcome.IsSuccess ? Ok(outcome.Value) : MapError(outcome.Error!);
        }

        var preview = await _import.PreviewAsync(farmId, stream, file.FileName, mapping, cancellationToken);
        return preview.IsSuccess ? Ok(preview.Value) : MapError(preview.Error!);
    }

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
