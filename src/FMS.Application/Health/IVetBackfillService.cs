namespace FMS.Application.Health;

/// <summary>
/// The second part of the vet migration: turning the retained <c>VetName</c> strings
/// on the four clinical tables into vet rows and linking the records. It is separate
/// from the EF migration because migrations run automatically when the API starts and
/// this step must be invokable as a dry run first.
/// </summary>
public interface IVetBackfillService
{
    Task<VetBackfillReport> RunAsync(bool apply, CancellationToken cancellationToken = default);
}
