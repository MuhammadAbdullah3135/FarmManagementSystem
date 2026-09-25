namespace FMS.Application.Farm.Export;

/// <summary>
/// One file inside the archive: what it holds and its shape.
///
/// The row count is the completeness evidence the manifest exists to carry — a
/// caller can compare it against the farm's own screens without opening the CSV.
/// </summary>
public class FarmExportFileDto
{
    /// <summary>Entry name inside the ZIP, e.g. <c>animals.csv</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The entity this file is, e.g. <c>Animals</c>.</summary>
    public string Entity { get; set; } = string.Empty;

    public int RowCount { get; set; }

    /// <summary>
    /// The CSV's header row, in order.
    ///
    /// For the seven entities that have an importer these are that importer's own
    /// field labels, which its auto-detection matches — so the file re-imports with
    /// no column mapping. That is a guarantee rather than a convention, and
    /// <c>FarmExportAssemblerTests</c> asserts it against the catalogs.
    /// </summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>True when this entity has an import endpoint the file can be fed back to unmodified.</summary>
    public bool Reimportable { get; set; }
}

/// <summary>A farm-scoped table deliberately left out of the archive, and why.</summary>
public class FarmExportExclusionDto
{
    /// <summary>The entity, e.g. <c>FarmHealthStatusSnapshot</c>.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The reason it is not in the archive, in the server's own words.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The archive's <c>manifest.json</c>: what is in the archive, what is not, and
/// when it was made.
///
/// It travels with the data (written into the ZIP) and is also stored on the
/// export row so the status endpoint can report it without opening the archive.
/// The disclosed limitations live here rather than only in the UI, so a caller
/// reading the file on disk sees them too.
/// </summary>
public class FarmExportManifestDto
{
    /// <summary>Bumped when a file's shape changes, so an old manifest is still readable.</summary>
    public int SchemaVersion { get; set; } = 1;

    public Guid FarmId { get; set; }

    public string FarmName { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    /// <summary>What produced the archive, so an unattended file is traceable.</summary>
    public string Generator { get; set; } = "FMS full-farm export";

    /// <summary>The encoding/line-ending contract, stated so a parser does not have to guess.</summary>
    public string ArchiveFormat { get; set; } = "zip of per-entity CSV; UTF-8 with BOM; \\n line endings";

    /// <summary>Sum of <see cref="FarmExportFileDto.RowCount"/> across every file.</summary>
    public int TotalRowCount { get; set; }

    public List<FarmExportFileDto> Files { get; set; } = new();

    public List<FarmExportExclusionDto> ExcludedEntities { get; set; } = new();

    /// <summary>Limitations a reader must know to avoid mistaking the archive for something it is not.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// The export as the API reports it: the request's state plus, once ready, the
/// manifest summary.
/// </summary>
public class FarmExportDto
{
    public Guid Id { get; set; }

    public Guid FarmId { get; set; }

    /// <summary>One of <c>FarmExportStatus</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid RequestedByUserId { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? FileName { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Present only when <see cref="Status"/> is Failed.</summary>
    public string? Error { get; set; }

    /// <summary>True when the archive can be downloaded right now.</summary>
    public bool IsReady { get; set; }

    /// <summary>Null until the build has produced a manifest.</summary>
    public FarmExportManifestDto? Manifest { get; set; }
}

/// <summary>
/// A stored archive resolved for a download: the path is a storage key, never a
/// caller-supplied value, and is always taken from the requesting farm's own
/// export row.
/// </summary>
public class FarmExportArchive
{
    public string StoragePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = FarmExportContentType.Zip;

    public long SizeBytes { get; set; }
}

/// <summary>The content types the export deals in, named once so nothing guesses a string.</summary>
public static class FarmExportContentType
{
    public const string Zip = "application/zip";
}
