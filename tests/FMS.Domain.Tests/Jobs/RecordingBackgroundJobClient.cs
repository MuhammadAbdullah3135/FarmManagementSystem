using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// Captures the jobs Hangfire would have created, so a test can assert what was enqueued
/// — and what deliberately was not — instead of asserting only on the record the job would
/// eventually write.
///
/// <para>
/// Registered in the E2E host for the same reason the job classes are registered there:
/// the test host runs no Hangfire server (its storage is PostgreSQL, which InMemory cannot
/// stand in for), so "enqueue" has to be observable at the client boundary while the job
/// itself is invoked directly, exactly as the worker would invoke it.
/// </para>
/// </summary>
public sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> Created { get; } = new();

    public string Create(Job job, IState state)
    {
        Created.Add(job);
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;

    /// <summary>The enqueued export jobs, as (farmId, exportId) pairs.</summary>
    public IReadOnlyList<(Guid FarmId, Guid ExportId)> ExportJobs() =>
        Created
            .Where(job => job.Type.Name == "FarmExportJob")
            .Select(job => (Guid.Parse(job.Args[0].ToString()!), Guid.Parse(job.Args[1].ToString()!)))
            .ToList();
}
