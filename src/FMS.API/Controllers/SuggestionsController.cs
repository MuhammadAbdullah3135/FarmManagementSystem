using FMS.Application.Suggestions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Read-only recent-value suggestions for fields that still store a label string.
/// The field is a selector into a closed whitelist, never a column name.
/// </summary>
[ApiController]
[Route("api/farms/{farmId:guid}/suggestions")]
[Authorize]
public class SuggestionsController : ControllerBase
{
    private readonly ISuggestionService _suggestionService;

    public SuggestionsController(ISuggestionService suggestionService)
    {
        _suggestionService = suggestionService;
    }

    [HttpGet("{field}")]
    public async Task<IActionResult> GetSuggestions(Guid farmId, string field)
    {
        // Supplier products are part of the inventory directory, so this field keeps
        // that directory's role gate. Medical values remain available to any member
        // who can already read the medical-record list.
        if (SuggestionFields.RequiresSupplierDirectoryRole(field) &&
            !User.IsInRole("Employee") &&
            !User.IsInRole("FarmManager") &&
            !User.IsInRole("SystemOwner"))
        {
            return Forbid();
        }

        var result = await _suggestionService.GetSuggestionsAsync(farmId, field);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error!.Code == "NotFound"
            ? NotFound(result.Error.Message)
            : StatusCode(StatusCodes.Status500InternalServerError, result.Error.Message);
    }
}
