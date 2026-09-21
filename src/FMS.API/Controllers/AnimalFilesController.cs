using FMS.Application.Animal;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/animals")]
[Authorize]
public class AnimalFilesController : ControllerBase
{
    private readonly IAnimalService _animalService;
    private readonly IFileStorageService _fileStorage;

    public AnimalFilesController(IAnimalService animalService, IFileStorageService fileStorage)
    {
        _animalService = animalService;
        _fileStorage = fileStorage;
    }

    [HttpPost("{animalId:guid}/images")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> AddImage(
        Guid farmId, Guid animalId,
        IFormFile file,
        [FromForm] string? caption,
        [FromForm] bool isPrimary = false)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required");

        await using var stream = file.OpenReadStream();
        var result = await _animalService.AddImageAsync(
            farmId, animalId, stream, file.FileName, file.ContentType, file.Length, caption, isPrimary);

        return result.IsSuccess ? Created("", result.Value) : MapError(result.Error!);
    }

    [HttpGet("{animalId:guid}/images")]
    public async Task<IActionResult> GetImages(Guid farmId, Guid animalId)
    {
        var result = await _animalService.GetImagesAsync(farmId, animalId);
        return MapResult(result);
    }

    [HttpDelete("{animalId:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid farmId, Guid animalId, Guid imageId)
    {
        var result = await _animalService.DeleteImageAsync(farmId, animalId, imageId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpPut("{animalId:guid}/images/{imageId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryImage(Guid farmId, Guid animalId, Guid imageId)
    {
        var result = await _animalService.SetPrimaryImageAsync(farmId, animalId, imageId);
        return MapResult(result);
    }

    [HttpPost("{animalId:guid}/documents")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> AddDocument(
        Guid farmId, Guid animalId,
        IFormFile file,
        [FromForm] string category,
        [FromForm] string? description)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required");

        await using var stream = file.OpenReadStream();
        var result = await _animalService.AddDocumentAsync(
            farmId, animalId, stream, file.FileName, file.ContentType, file.Length, category, description);

        return result.IsSuccess ? Created("", result.Value) : MapError(result.Error!);
    }

    [HttpGet("{animalId:guid}/documents")]
    public async Task<IActionResult> GetDocuments(Guid farmId, Guid animalId, [FromQuery] string? category)
    {
        var result = await _animalService.GetDocumentsAsync(farmId, animalId, category);
        return MapResult(result);
    }

    [HttpDelete("{animalId:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid farmId, Guid animalId, Guid documentId)
    {
        var result = await _animalService.DeleteDocumentAsync(farmId, animalId, documentId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpGet("{animalId:guid}/images/{imageId:guid}/download")]
    public async Task<IActionResult> DownloadImage(Guid farmId, Guid animalId, Guid imageId)
    {
        var result = await _animalService.GetImageForDownloadAsync(farmId, animalId, imageId);
        return await ServeStoredFileAsync(result);
    }

    [HttpGet("{animalId:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid farmId, Guid animalId, Guid documentId)
    {
        var result = await _animalService.GetDocumentForDownloadAsync(farmId, animalId, documentId);
        return await ServeStoredFileAsync(result);
    }

    /// <summary>
    /// Serves a stored file to a caller already authorized to read this farm's animal.
    ///
    /// The authorization above is what gates access: on object storage we hand back a
    /// short-lived presigned URL so the bytes come straight from the bucket, and on local
    /// disk we stream them instead. Either way the file is never publicly addressable.
    /// </summary>
    private async Task<IActionResult> ServeStoredFileAsync(Result<AnimalFileDownload> result)
    {
        if (!result.IsSuccess)
            return MapError(result.Error!);

        var file = result.Value!;

        var presigned = await _fileStorage.GetPresignedDownloadUrlAsync(file.StoragePath, file.OriginalFileName);
        if (presigned.IsSuccess)
            return Redirect(presigned.Value!.Url);

        var stream = await _fileStorage.OpenReadAsync(file.StoragePath);
        return File(stream, file.ContentType, file.OriginalFileName);
    }

    [HttpPost("{animalId:guid}/transfer")]
    public async Task<IActionResult> TransferAnimal(Guid farmId, Guid animalId, [FromBody] TransferAnimalRequest request)
    {
        var result = await _animalService.TransferAnimalAsync(farmId, animalId, request);
        return MapResult(result);
    }

    [HttpPost("bulk/status")]
    public async Task<IActionResult> BulkChangeStatus(Guid farmId, [FromBody] BulkStatusChangeRequest request)
    {
        var result = await _animalService.BulkChangeStatusAsync(farmId, request);
        return MapResult(result);
    }

    [HttpPost("bulk/transfer")]
    public async Task<IActionResult> BulkTransfer(Guid farmId, [FromBody] BulkTransferRequest request)
    {
        var result = await _animalService.BulkTransferAsync(farmId, request);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
