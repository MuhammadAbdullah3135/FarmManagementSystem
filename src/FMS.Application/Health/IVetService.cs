using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IVetService
{
    Task<Result<List<VetDto>>> GetVetsAsync(Guid farmId, bool includeInactive);
    Task<Result<VetDto>> GetVetByIdAsync(Guid farmId, Guid id);
    Task<Result<VetDto>> CreateVetAsync(Guid farmId, CreateVetRequest request);
    Task<Result<VetDto>> UpdateVetAsync(Guid farmId, Guid id, UpdateVetRequest request);
    Task<Result> DeleteVetAsync(Guid farmId, Guid id);
}
