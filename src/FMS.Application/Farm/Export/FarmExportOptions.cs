namespace FMS.Application.Farm.Export;

/// <summary>
/// Full-farm export configuration, bound from the <c>Export</c> configuration
/// section. Every value has a working default, so the API runs unconfigured.
/// </summary>
public class FarmExportOptions
{
    public const string SectionName = "Export";

    /// <summary>
    /// Ceiling on the stored archive, enforced by <c>IFileStorageService.SaveAsync</c>
    /// when the job uploads it.
    ///
    /// A deliberate refusal rather than a best effort: an export that silently
    /// produced a truncated archive would look complete to whoever downloaded it,
    /// and a partial copy of a farm's records is worse than a clearly failed one.
    /// </summary>
    public long MaxArchiveBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Whether the downloaded archive's file name carries the farm's name
    /// (<c>acme-farm-export-2026-09-24.zip</c>) rather than a bare id. On by
    /// default: a person who downloads several farms' archives should be able to
    /// tell them apart in their downloads folder.
    /// </summary>
    public bool IncludeFarmNameInFileName { get; set; } = true;
}
