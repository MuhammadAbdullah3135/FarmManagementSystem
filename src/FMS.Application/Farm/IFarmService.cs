using FMS.Application.Common;

namespace FMS.Application.Farm;

public interface IFarmService
{
    Task<Result<FarmDto>> CreateFarmAsync(Guid accountId, Guid userId, CreateFarmRequest request);
    Task<Result<FarmDto>> GetFarmAsync(Guid farmId, Guid userId);
    Task<Result<List<FarmDto>>> GetUserFarmsAsync(Guid userId);
    Task<Result<FarmDto>> UpdateFarmAsync(Guid farmId, Guid userId, UpdateFarmRequest request);
    Task<Result> DeleteFarmAsync(Guid farmId, Guid userId);
}
