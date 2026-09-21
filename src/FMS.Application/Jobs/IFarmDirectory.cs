namespace FMS.Application.Jobs;

/// <summary>
/// Reads the active farm list for background fan-out jobs.
///
/// Returns identifiers only: the fan-out must never load cross-farm data into
/// a single unit of work. Each id is then handed to a per-farm job that runs in
/// its own service scope, which is what keeps the multi-tenant boundary intact
/// outside a request.
/// </summary>
public interface IFarmDirectory
{
    Task<IReadOnlyList<Guid>> GetActiveFarmIdsAsync(CancellationToken cancellationToken = default);
}
