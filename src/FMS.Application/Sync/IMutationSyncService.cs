namespace FMS.Application.Sync;

/// <summary>
/// Applies a batch of queued offline mutations. Each item is applied by the workflow's own
/// service method, never by a parallel implementation, so the sync path cannot accept what the
/// single-record endpoint refuses or answer with different words.
/// </summary>
public interface IMutationSyncService
{
    Task<SyncMutationResultDto> ApplyAsync(
        Guid farmId, SyncMutationRequest request, CancellationToken cancellationToken = default);
}
