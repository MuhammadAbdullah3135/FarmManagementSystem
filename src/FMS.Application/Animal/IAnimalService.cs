using FMS.Application.Common;

namespace FMS.Application.Animal;

public interface IAnimalService
{
    Task<Result<PagedResult<AnimalListItemDto>>> GetAnimalsAsync(Guid farmId, AnimalListFilter filter);
    Task<Result<AnimalDetailDto>> GetAnimalByIdAsync(Guid farmId, Guid id);
    Task<Result<AnimalDetailDto>> CreateAnimalAsync(Guid farmId, CreateAnimalRequest request);
    Task<Result<AnimalDetailDto>> UpdateAnimalAsync(Guid farmId, Guid id, UpdateAnimalRequest request);
    Task<Result> DeleteAnimalAsync(Guid farmId, Guid id);
    Task<Result<AnimalDetailDto>> ChangeStatusAsync(Guid farmId, Guid id, ChangeAnimalStatusRequest request);
    Task<Result<AnimalIdentificationDto>> AddIdentificationAsync(Guid farmId, Guid animalId, AddAnimalIdentificationRequest request);
    Task<Result> RemoveIdentificationAsync(Guid farmId, Guid animalId, Guid identificationId);

    Task<Result<PagedResult<WeightRecordDto>>> GetWeightsAsync(Guid farmId, Guid animalId, int page, int pageSize);
    Task<Result<WeightRecordDto>> AddWeightAsync(Guid farmId, Guid animalId, CreateWeightRecordRequest request);
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
    Task<Result<AnimalDocumentDto>> GetDocumentForDownloadAsync(Guid farmId, Guid animalId, Guid documentId);

    Task<Result<AnimalTransferDto>> TransferAnimalAsync(Guid farmId, Guid animalId, TransferAnimalRequest request);
    Task<Result<BulkOperationResultDto>> BulkChangeStatusAsync(Guid farmId, BulkStatusChangeRequest request);
    Task<Result<BulkOperationResultDto>> BulkTransferAsync(Guid farmId, BulkTransferRequest request);

    Task<Result<PagedResult<AnimalTimelineEventDto>>> GetTimelineAsync(Guid farmId, Guid animalId, int page, int pageSize, string? eventType);
}
