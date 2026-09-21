namespace FMS.Application.Animal.Import;

/// <summary>
/// Where one canonical field's value comes from: a column in the uploaded file, or
/// a single value applied to every row.
///
/// The constant form exists because real spreadsheets rarely carry a status or sex
/// column — "everything in this file is Active" is the common case.
/// </summary>
public class AnimalImportFieldMap
{
    /// <summary>Zero-based index into the file's header row.</summary>
    public int? Column { get; set; }

    /// <summary>Value applied to every row. Ignored when <see cref="Column"/> is set.</summary>
    public string? Constant { get; set; }

    public bool IsMapped => Column.HasValue || !string.IsNullOrWhiteSpace(Constant);
}

/// <summary>A field key to source mapping, plus the per-import date format override.</summary>
public class AnimalImportMapping
{
    public Dictionary<string, AnimalImportFieldMap> Fields { get; set; } = new();

    /// <summary>
    /// Format used to read date columns, e.g. <c>dd/MM/yyyy</c>. Supplying it is how
    /// a caller resolves a date that would otherwise be ambiguous.
    /// </summary>
    public string? DateFormat { get; set; }

    public AnimalImportFieldMap? For(string key) =>
        Fields.TryGetValue(key, out var map) ? map : null;
}

public class AnimalImportRowErrorDto
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// One row of the file, as read (not as stored). <see cref="Values"/> keeps the raw
/// mapped text per field so the preview can show exactly what was picked up.
/// </summary>
public class AnimalImportRowDto
{
    /// <summary>Spreadsheet row number, so the user can find the row in their file.</summary>
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public List<AnimalImportRowErrorDto> Errors { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>
/// The validation preview. Nothing has been written when this is returned.
///
/// <see cref="Fields"/>, <see cref="Headers"/> and <see cref="Lookups"/> are what
/// the mapping step needs, so uploading a file and asking for a preview is enough
/// to build the whole wizard — the mapping tables come from the file that is
/// actually being imported rather than a hard-coded list on the client.
/// </summary>
public class AnimalImportPreviewDto
{
    public List<AnimalImportFieldDescriptor> Fields { get; set; } = new();
    public List<string> Headers { get; set; } = new();

    /// <summary>The mapping the results below were produced with.</summary>
    public AnimalImportMapping Mapping { get; set; } = new();

    /// <summary>
    /// What header auto-detection would choose for this file. Sent even when the
    /// caller supplied a mapping, so the wizard can offer to reset to the guess.
    /// </summary>
    public AnimalImportMapping SuggestedMapping { get; set; } = new();

    /// <summary>
    /// The farm's existing lookup names per lookup field, so a "fixed value" mapping
    /// offers real options instead of a free-text box.
    /// </summary>
    public Dictionary<string, List<string>> Lookups { get; set; } = new();

    public int TotalRows { get; set; }
    public int ValidRowCount { get; set; }
    public int InvalidRowCount { get; set; }

    /// <summary>True when problem rows beyond <c>MaxReportedRows</c> were omitted.</summary>
    public bool Truncated { get; set; }

    /// <summary>Every row that failed validation, capped by <c>MaxReportedRows</c>.</summary>
    public List<AnimalImportRowDto> InvalidRows { get; set; } = new();

    /// <summary>A few passing rows, so the mapping can be eyeballed before committing.</summary>
    public List<AnimalImportRowDto> SampleValidRows { get; set; } = new();
}

/// <summary>
/// The commit outcome. Because the commit is all-or-nothing,
/// <see cref="ImportedCount"/> is either zero or <see cref="TotalRows"/>.
/// </summary>
public class AnimalImportCommitDto
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public bool Truncated { get; set; }
    public List<AnimalImportRowDto> InvalidRows { get; set; } = new();
}

/// <summary>
/// Import limits, bound from the <c>AnimalImport</c> configuration section, in the
/// shape the job and notification options already established.
/// </summary>
public class AnimalImportOptions
{
    public const string SectionName = "AnimalImport";

    /// <summary>Transport-level guard shared with the controller's request size limit.</summary>
    public const long DefaultMaxFileBytes = 10 * 1024 * 1024;

    public int MaxRows { get; set; } = 5000;
    public long MaxFileBytes { get; set; } = DefaultMaxFileBytes;

    /// <summary>Caps how many problem rows a response carries, not how many exist.</summary>
    public int MaxReportedRows { get; set; } = 500;

    public int SampleValidRows { get; set; } = 10;
}
