using FMS.Application.Common;
using FMS.Application.Import;

namespace FMS.Application.Inventory.Import;

/// <summary>
/// Bulk customer import: validate a file without writing, then commit it atomically.
///
/// The same two-call contract as every other importer: the commit re-reads and
/// re-validates the bytes it is given rather than trusting the preview.
/// </summary>
public interface ICustomerImportService
{
    Task<Result<ImportPreviewDto>> PreviewAsync(
        Guid farmId,
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken = default);

    Task<Result<ImportCommitDto>> CommitAsync(
        Guid farmId,
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken = default);
}
