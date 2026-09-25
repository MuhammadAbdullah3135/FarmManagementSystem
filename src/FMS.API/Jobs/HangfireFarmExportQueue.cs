using FMS.Application.Farm.Export;
using FMS.Infrastructure.Jobs;
using Hangfire;

namespace FMS.API.Jobs;

/// <summary>
/// Runs export builds through Hangfire, joining the other jobs in this project's storage.
///
/// <para>
/// One argument per farm, so the job's own scope owns the transaction — the same
/// per-farm isolation the scheduled jobs rely on. It is enqueued rather than awaited by
/// the caller: the request has already returned by the time this work starts.
/// </para>
/// </summary>
public class HangfireFarmExportQueue : IFarmExportQueue
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireFarmExportQueue(IBackgroundJobClient jobs)
    {
        _jobs = jobs;
    }

    public void Enqueue(Guid farmId, Guid exportId) =>
        _jobs.Enqueue<FarmExportJob>(job => job.ExecuteAsync(farmId, exportId));
}
