using FMS.Application.Common;

namespace FMS.Application.Animal;

public interface IAnimalService
{
    Task<Result<PagedResult<AnimalListItemDto>>> GetAnimalsAsync(Guid farmId, AnimalListFilter filter);
    Task<Result<AnimalDetailDto>> GetAnimalByIdAsync(Guid farmId, Guid id);

    /// <summary>
    /// The QR label for one animal: the payload that goes on the physical tag, built by the
    /// server so the printed code and the code that resolves back to this animal are one
    /// definition rather than two that agree until one is changed.
    /// </summary>
    Task<Result<AnimalQrLabelDto>> GetQrLabelAsync(Guid farmId, Guid id);

    /// <summary>
    /// A bounded page of QR labels for a label sheet, through the same filter, ordering and
    /// paging a <see cref="GetAnimalsAsync"/> call applies — so the sheet prints the rows the
    /// user is looking at rather than its own idea of which animals belong on it. A caller
    /// wanting a large sheet asks for a large <see cref="AnimalListFilter.PageSize"/>.
    /// </summary>
    Task<Result<PagedResult<AnimalQrLabelDto>>> GetQrLabelsAsync(Guid farmId, AnimalListFilter filter);
    Task<Result<AnimalDetailDto>> CreateAnimalAsync(Guid farmId, CreateAnimalRequest request);
    Task<Result<AnimalDetailDto>> UpdateAnimalAsync(Guid farmId, Guid id, UpdateAnimalRequest request);
    Task<Result> DeleteAnimalAsync(Guid farmId, Guid id);
    Task<Result<AnimalDetailDto>> ChangeStatusAsync(Guid farmId, Guid id, ChangeAnimalStatusRequest request);
    Task<Result<AnimalIdentificationDto>> AddIdentificationAsync(Guid farmId, Guid animalId, AddAnimalIdentificationRequest request);
    Task<Result> RemoveIdentificationAsync(Guid farmId, Guid animalId, Guid identificationId);

    Task<Result<PagedResult<WeightRecordDto>>> GetWeightsAsync(Guid farmId, Guid animalId, int page, int pageSize);
    Task<Result<WeightRecordDto>> AddWeightAsync(Guid farmId, Guid animalId, CreateWeightRecordRequest request, Guid? mutationId = null);
    Task<Result<WeightRecordDto>> UpdateWeightAsync(Guid farmId, Guid animalId, Guid weightId, UpdateWeightRecordRequest request);
    Task<Result> DeleteWeightAsync(Guid farmId, Guid animalId, Guid weightId);
    Task<Result<WeightReportDto>> GetWeightReportAsync(Guid farmId, Guid animalId);

    Task<Result<AnimalImageDto>> AddImageAsync(Guid farmId, Guid animalId, Stream content, string originalFileName, string contentType, long contentLength, string? caption, bool isPrimary);
    Task<Result<List<AnimalImageDto>>> GetImagesAsync(Guid farmId, Guid animalId);
    Task<Result> DeleteImageAsync(Guid farmId, Guid animalId, Guid imageId);
    Task<Result<AnimalImageDto>> SetPrimaryImageAsync(Guid farmId, Guid animalId, Guid imageId);

    Task<Result<AnimalDocumentDto>> AddDocumentAsync(Guid farmId, Guid animalId, Stream content, string originalFileName, string contentType, long contentLength, string category, string? description);
    Task<Result<List<AnimalDocumentDto>>> GetDocumentsAsync(Guid farmId, Guid animalId, string? category);
    Task<Result> DeleteDocumentAsync(Guid farmId, Guid animalId, Guid documentId);

    /// <summary>
    /// Resolves a document for download. Callers MUST authorize before using the result:
    /// it is the gate that decides who may read the stored bytes.
    /// </summary>
    Task<Result<AnimalFileDownload>> GetDocumentForDownloadAsync(Guid farmId, Guid animalId, Guid documentId);

    /// <summary>Resolves an image for download. Authorization is the caller's responsibility.</summary>
    Task<Result<AnimalFileDownload>> GetImageForDownloadAsync(Guid farmId, Guid animalId, Guid imageId);

    /// <summary>
    /// Validates a batch of animal creations against exactly the rules a single
    /// create uses and writes nothing, reporting failures by batch index.
    ///
    /// The preview half of a bulk import: identical rules to
    /// <see cref="CreateAnimalsAsync"/>, so what it predicts is what a commit does.
    /// </summary>
    Task<Result<BulkAnimalCreateResultDto>> ValidateAnimalsAsync(Guid farmId, IReadOnlyList<BulkAnimalCreateItem> items);

    /// <summary>
    /// Validates then creates the whole batch in a single SaveChanges, which EF
    /// wraps in one transaction — so the batch is atomic: either every animal is
    /// written or none is. Callers supply the ids (see <see cref="BulkAnimalCreateItem.Id"/>),
    /// which is what allows rows in the batch to reference one another.
    /// </summary>
    Task<Result<BulkAnimalCreateResultDto>> CreateAnimalsAsync(Guid farmId, IReadOnlyList<BulkAnimalCreateItem> items);

    Task<Result<AnimalTransferDto>> TransferAnimalAsync(Guid farmId, Guid animalId, TransferAnimalRequest request);
    Task<Result<BulkOperationResultDto>> BulkChangeStatusAsync(Guid farmId, BulkStatusChangeRequest request);
    Task<Result<BulkOperationResultDto>> BulkTransferAsync(Guid farmId, BulkTransferRequest request);

    Task<Result<PagedResult<AnimalTimelineEventDto>>> GetTimelineAsync(Guid farmId, Guid animalId, int page, int pageSize, string? eventType);
}
