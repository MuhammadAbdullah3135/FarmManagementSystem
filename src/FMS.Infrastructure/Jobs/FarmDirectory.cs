using FMS.Application.Jobs;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Jobs;

/// <inheritdoc />
public class FarmDirectory : IFarmDirectory
{
    private readonly FmsDbContext _db;

    public FarmDirectory(FmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveFarmIdsAsync(CancellationToken cancellationToken = default)
    {
        // Projected to ids only: the fan-out must not materialise any farm row,
        // and certainly not any farm-owned data, in a shared unit of work.
        return await _db.Farms
            .AsNoTracking()
            .Where(f => !f.IsDeleted && f.IsActive)
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
    }
}
