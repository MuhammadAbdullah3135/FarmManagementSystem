using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface IInventoryCategoryService
{
    Task<Result<List<InventoryCategoryDto>>> GetCategoriesAsync(Guid farmId, bool includeInactive);
    Task<Result<InventoryCategoryDto>> GetCategoryByIdAsync(Guid farmId, Guid id);
    Task<Result<InventoryCategoryDto>> CreateCategoryAsync(Guid farmId, CreateInventoryCategoryRequest request);
    Task<Result<InventoryCategoryDto>> UpdateCategoryAsync(Guid farmId, Guid id, UpdateInventoryCategoryRequest request);
    Task<Result> DeleteCategoryAsync(Guid farmId, Guid id);
}
