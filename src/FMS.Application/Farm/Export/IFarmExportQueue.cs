namespace FMS.Application.Farm.Export;

/// <summary>
/// The build side of the export: handing a decided-upon request to whatever runs jobs.
///
/// <para>
/// An abstraction rather than the job client itself, because the job subsystem belongs to
/// the host layer: nothing below the API may know that the work runs on Hangfire, or the
/// first deployment that swaps the queue would move a dependency into a project that
/// does not otherwise have it.
/// </para>
///
/// <para>
/// Deliberately not a Task: enqueueing is a handoff, not work. A caller that awaited it
/// would be awaiting the archive and would be back to holding a request open for the
/// farm's whole history — the exact thing this design exists to avoid.
/// </para>
/// </summary>
public interface IFarmExportQueue
{
    /// <summary>
    /// Queues one build for one farm.
    ///
    /// Called only after the caller has decided the request is acceptable and has recorded
    /// it, so the queue never has to decide anything — including whether jobs are enabled.
    /// </summary>
    void Enqueue(Guid farmId, Guid exportId);
}
