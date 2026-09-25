using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface ISupplierService
{
    Task<Result<PagedResult<SupplierDto>>> GetSuppliersAsync(Guid farmId, SupplierListFilter filter);
    Task<Result<SupplierDto>> GetSupplierAsync(Guid farmId, Guid id);
    Task<Result<SupplierDto>> CreateSupplierAsync(Guid farmId, CreateSupplierRequest request);

    /// <summary>
    /// Validates a batch without writing it. Same rules and same duplicate check as
    /// <see cref="CreateSupplierAsync"/>, reported per row, so a bulk caller can preview
    /// a file and get exactly the answer a commit would give.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateSuppliersAsync(Guid farmId, IReadOnlyList<CreateSupplierRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateSuppliersAsync(Guid farmId, IReadOnlyList<CreateSupplierRequest> requests);
    Task<Result<SupplierDto>> UpdateSupplierAsync(Guid farmId, Guid id, UpdateSupplierRequest request);
    Task<Result> DeleteSupplierAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<SupplierPurchaseDto>>> GetPurchasesAsync(Guid farmId, SupplierPurchaseListFilter filter);
    Task<Result<SupplierPurchaseDto>> CreatePurchaseAsync(Guid farmId, CreateSupplierPurchaseRequest request);
}
