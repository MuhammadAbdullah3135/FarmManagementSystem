using FMS.Application.Common;
using FMS.Application.Import;

namespace FMS.Application.Animal.Import;

/// <summary>
/// Bulk animal import: validate a file without writing, then commit it atomically.
///
/// Both calls run the same pipeline over the same bytes, and the commit re-reads and
/// re-validates the file rather than trusting the preview, so a caller cannot smuggle
/// a row past validation by editing anything between the two calls.
/// </summary>
public interface IAnimalImportService
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
