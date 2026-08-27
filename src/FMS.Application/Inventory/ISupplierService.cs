using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface ISupplierService
{
    Task<Result<PagedResult<SupplierDto>>> GetSuppliersAsync(Guid farmId, SupplierListFilter filter);
    Task<Result<SupplierDto>> GetSupplierAsync(Guid farmId, Guid id);
    Task<Result<SupplierDto>> CreateSupplierAsync(Guid farmId, CreateSupplierRequest request);
    Task<Result<SupplierDto>> UpdateSupplierAsync(Guid farmId, Guid id, UpdateSupplierRequest request);
    Task<Result> DeleteSupplierAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<SupplierPurchaseDto>>> GetPurchasesAsync(Guid farmId, SupplierPurchaseListFilter filter);
    Task<Result<SupplierPurchaseDto>> CreatePurchaseAsync(Guid farmId, CreateSupplierPurchaseRequest request);
}
