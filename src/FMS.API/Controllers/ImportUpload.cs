using System.Text.Json;
using FMS.Application.Common;
using FMS.Application.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// The parts of an import request that are identical for every entity: the upload
/// itself, the column mapping that arrives as a JSON form field, and the mapping from
/// a service error to a status code.
///
/// Shared so the three import controllers cannot drift apart in how they refuse a
/// request — one of them accepting a 12 MB file because its size check was forgotten
/// is exactly the kind of difference nobody notices until it matters.
/// </summary>
public static class ImportUpload
{
    private static readonly JsonSerializerOptions MappingJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Why the upload cannot be used, or null when it can.</summary>
    public static string? Validate(IFormFile? file, ImportOptions options)
    {
        if (file is null || file.Length == 0)
            return "A file is required.";

        if (file.Length > options.MaxFileBytes)
            return $"The file is {file.Length / (1024.0 * 1024.0):0.#} MB. The limit for one import is " +
                $"{options.MaxFileBytes / (1024.0 * 1024.0):0.#} MB.";

        return null;
    }

    /// <summary>
    /// Reads the mapping form field. Absent means "auto-detect"; present but
    /// unreadable is a bad request rather than a silent fallback, because quietly
    /// auto-detecting after a mapping failed to parse would import the wrong columns.
    /// </summary>
    public static bool TryDecodeMapping(string? json, out ImportMapping? mapping, out string? error)
    {
        mapping = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            mapping = JsonSerializer.Deserialize<ImportMapping>(json, MappingJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            error = "The column mapping could not be read.";
            return false;
        }
    }

    /// <summary>The service error's status code, in the shape the rest of the API uses.</summary>
    public static IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => new NotFoundObjectResult(error.Message),
        "Validation" => new BadRequestObjectResult(error.Message),
        "Conflict" => new ConflictObjectResult(error.Message),
        "Unauthorized" => new UnauthorizedObjectResult(error.Message),
        _ => new ObjectResult(error.Message) { StatusCode = 500 }
    };
}
