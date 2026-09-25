using FMS.Application.Common;
using FMS.Application.Import;

namespace FMS.Application.Finance.Import;

/// <summary>
/// Bulk expense import: validate a file without writing, then commit it atomically.
///
/// The same two-call contract as every other importer. The one deliberate difference is
/// the duplicate policy: an expense has no identifier, so there is no duplicate rule,
/// which is disclosed rather than invented.
/// </summary>
public interface IExpenseImportService
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
