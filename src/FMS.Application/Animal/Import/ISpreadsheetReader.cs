using FMS.Application.Common;

namespace FMS.Application.Animal.Import;

/// <summary>
/// A spreadsheet row with its original row number, so a reported problem points at
/// the row the user can actually find in their file.
/// </summary>
public class SpreadsheetRow
{
    public int RowNumber { get; init; }
    public IReadOnlyList<string> Cells { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A parsed sheet: the header row plus the data rows, with every cell normalized to
/// trimmed text.
///
/// Normalization is the point of this contract — date cells are formatted as
/// ISO-8601 here, so the import pipeline never has to care whether a value arrived
/// from a CSV or from a real Excel date cell.
/// </summary>
public class SpreadsheetSheet
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SpreadsheetRow> Rows { get; init; } = Array.Empty<SpreadsheetRow>();
}

/// <summary>
/// Reads an uploaded CSV or Excel file.
///
/// Format-agnostic so the import pipeline above it (mapping, validation, commit)
/// is written once. A later employee or inventory import supplies its own fields
/// and resolver and reuses this reader and that pipeline, rather than a new
/// framework.
/// </summary>
public interface ISpreadsheetReader
{
    Task<Result<SpreadsheetSheet>> ReadAsync(
        Stream content,
        string fileName,
        int maxRows,
        CancellationToken cancellationToken = default);
}
