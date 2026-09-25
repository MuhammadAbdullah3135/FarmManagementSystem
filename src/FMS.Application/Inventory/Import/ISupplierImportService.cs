using FMS.Application.Common;
using FMS.Application.Import;

namespace FMS.Application.Inventory.Import;

/// <summary>
/// Bulk supplier import: validate a file without writing, then commit it atomically.
///
/// The same two-call contract as the animal, employee and inventory importers,
/// deliberately: the commit re-reads and re-validates the bytes it is given rather than
/// trusting the preview, so nothing a caller changes between the two calls can get a row
/// past validation.
/// </summary>
public interface ISupplierImportService
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
