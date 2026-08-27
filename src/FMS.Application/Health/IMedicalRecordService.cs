using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IMedicalRecordService
{
    Task<Result<PagedResult<MedicalRecordListItemDto>>> GetMedicalRecordsAsync(Guid farmId, MedicalRecordListFilter filter);
    Task<Result<MedicalRecordDto>> GetMedicalRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<MedicalRecordListItemDto>>> GetMedicalRecordsByAnimalAsync(Guid farmId, Guid animalId, int page, int pageSize);
    Task<Result<MedicalRecordDto>> CreateMedicalRecordAsync(Guid farmId, CreateMedicalRecordRequest request);
    Task<Result<MedicalRecordDto>> UpdateMedicalRecordAsync(Guid farmId, Guid id, UpdateMedicalRecordRequest request);
    Task<Result> DeleteMedicalRecordAsync(Guid farmId, Guid id);
}
