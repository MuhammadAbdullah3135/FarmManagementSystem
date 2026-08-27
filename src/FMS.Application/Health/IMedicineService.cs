using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IMedicineService
{
    // Medicine CRUD
    Task<Result<PagedResult<MedicineListItemDto>>> GetMedicinesAsync(Guid farmId, int page, int pageSize, string? search);
    Task<Result<MedicineDto>> GetMedicineByIdAsync(Guid farmId, Guid id);
    Task<Result<MedicineDto>> CreateMedicineAsync(Guid farmId, CreateMedicineRequest request);
    Task<Result<MedicineDto>> UpdateMedicineAsync(Guid farmId, Guid id, UpdateMedicineRequest request);
    Task<Result> DeleteMedicineAsync(Guid farmId, Guid id);

    // Stock batch management
    Task<Result<List<MedicineStockDto>>> GetStockBatchesAsync(Guid farmId, Guid medicineId);
    Task<Result<MedicineStockDto>> AddStockBatchAsync(Guid farmId, Guid medicineId, AddStockBatchRequest request);
    Task<Result> DeleteStockBatchAsync(Guid farmId, Guid medicineId, Guid stockId);

    // Usage recording (FIFO deduction)
    Task<Result<MedicineUsageDto>> RecordUsageAsync(Guid farmId, RecordMedicineUsageRequest request);

    // Alerts
    Task<Result<List<MedicineAlertDto>>> GetAlertsAsync(Guid farmId);
}
