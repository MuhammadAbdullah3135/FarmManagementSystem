using FMS.Application.Common;

namespace FMS.Application.Farm.Export;

/// <summary>
/// The farm's full-data export: request it, read its state, fetch the archive.
///
/// <para>
/// Every method takes the farm explicitly and the implementation filters on it.
/// The archive's storage key is always taken from the farm's own export row, never
/// from the caller, so a download cannot address another farm's file even if a
/// path is somehow supplied.
/// </para>
///
/// <para>
/// Building the archive is not done here: <see cref="RequestAsync"/> records the
/// request and hands it to the background job system, because the work is bounded
/// by the farm's history rather than by anything the request can control. See
/// <c>FarmExportJob</c>.
/// </para>
/// </summary>
public interface IFarmExportService
{
    /// <summary>
    /// Queues an export of this farm, or returns the one already in flight.
    ///
    /// Idempotent while a build is running (a double-click must not enqueue a
    /// second job over the same farm), and refuses outright when the job
    /// subsystem is disabled — accepting work nothing will ever run would be a
    /// request that silently never completes.
    /// </summary>
    Task<Result<FarmExportDto>> RequestAsync(Guid farmId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The farm's export state, with the manifest summary once an archive exists. Never any
    /// bytes.
    ///
    /// A null value means this farm has never been exported, which is an ordinary state rather
    /// than a failed request.
    /// </summary>
    Task<Result<FarmExportDto?>> GetLatestAsync(Guid farmId, CancellationToken cancellationToken = default);

    /// <summary>The stored archive of the farm's completed export, for download.</summary>
    Task<Result<FarmExportArchive>> OpenArchiveAsync(Guid farmId, CancellationToken cancellationToken = default);
}
