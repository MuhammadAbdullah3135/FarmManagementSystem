using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IVaccineService
{
    // VaccineType CRUD
    Task<Result<PagedResult<VaccineTypeListItemDto>>> GetVaccineTypesAsync(Guid farmId, int page, int pageSize, string? search);
    Task<Result<VaccineTypeDto>> GetVaccineTypeByIdAsync(Guid farmId, Guid id);
    Task<Result<VaccineTypeDto>> CreateVaccineTypeAsync(Guid farmId, CreateVaccineTypeRequest request);
    Task<Result<VaccineTypeDto>> UpdateVaccineTypeAsync(Guid farmId, Guid id, UpdateVaccineTypeRequest request);
    Task<Result> DeleteVaccineTypeAsync(Guid farmId, Guid id);

    // VaccinationRecord CRUD
    Task<Result<PagedResult<VaccinationRecordListItemDto>>> GetVaccinationRecordsAsync(Guid farmId, VaccinationRecordListFilter filter);
    Task<Result<VaccinationRecordDto>> GetVaccinationRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<VaccinationRecordListItemDto>>> GetVaccinationRecordsByAnimalAsync(Guid farmId, Guid animalId, int page, int pageSize);
    Task<Result<VaccinationRecordDto>> CreateVaccinationRecordAsync(Guid farmId, CreateVaccinationRecordRequest request);
    Task<Result<VaccinationRecordDto>> UpdateVaccinationRecordAsync(Guid farmId, Guid id, UpdateVaccinationRecordRequest request);
    Task<Result> DeleteVaccinationRecordAsync(Guid farmId, Guid id);

    // VaccinationSchedule CRUD
    Task<Result<PagedResult<VaccinationScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize);
    Task<Result<VaccinationScheduleDto>> CreateScheduleAsync(Guid farmId, CreateVaccinationScheduleRequest request);
    Task<Result<VaccinationScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateVaccinationScheduleRequest request);
    Task<Result> DeleteScheduleAsync(Guid farmId, Guid id);

    // Vaccination status
    Task<Result<List<VaccinationStatusDto>>> GetVaccinationStatusAsync(Guid farmId);
    Task<Result<List<VaccinationStatusDto>>> GetOverdueVaccinationsAsync(Guid farmId);
}
