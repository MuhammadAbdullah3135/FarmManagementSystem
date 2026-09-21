using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using FMS.Application.Import;
using FMS.Application.Common;

namespace FMS.Infrastructure.Import;

/// <summary>
/// Reads an uploaded CSV or .xlsx file into headers plus normalized text cells.
///
/// Both formats land on the same representation, which is what lets the import
/// pipeline above it (column mapping, row validation, commit) be written once. Date
/// cells are formatted as ISO-8601 as they are read, so a real Excel date and a
/// hand-typed <c>2023-04-15</c> behave identically downstream.
///
/// The reader never throws for bad input: an unreadable file is a validation error
/// the user can act on, not a 500.
/// </summary>
public class SpreadsheetReader : ISpreadsheetReader
{
    private const string CsvExtension = ".csv";
    private const string XlsxExtension = ".xlsx";
    private const string LegacyExcelExtension = ".xls";

    public Task<Result<SpreadsheetSheet>> ReadAsync(
        Stream content,
        string fileName,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return Task.FromResult(extension switch
        {
            CsvExtension => ReadCsv(content, maxRows),
            XlsxExtension => ReadWorkbook(content, maxRows),
            LegacyExcelExtension => Result<SpreadsheetSheet>.Validation(
                "The legacy .xls format is not supported. Save the file as .xlsx or CSV and upload it again."),
            _ => Result<SpreadsheetSheet>.Validation(
                $"'{extension}' files are not supported. Upload a CSV or .xlsx file.")
        });
    }

    private static Result<SpreadsheetSheet> ReadCsv(Stream content, int maxRows)
    {
        // UTF-8 with BOM detection, which is exactly what this app's own export
        // produces, plus plain UTF-8 without a BOM.
        using var streamReader = new StreamReader(
            content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = true,
            // Ragged or odd data must not abort the whole file: the row-level
            // validation is what tells the user which row is wrong and why.
            BadDataFound = null,
            MissingFieldFound = null,
        };

        try
        {
            using var csv = new CsvReader(streamReader, configuration);

            if (!csv.Read())
                return Result<SpreadsheetSheet>.Validation("The file is empty.");

            csv.ReadHeader();
            var headers = NormalizeHeaders(csv.HeaderRecord ?? Array.Empty<string>());

            var rows = new List<SpreadsheetRow>();
            while (csv.Read())
            {
                if (rows.Count >= maxRows)
                    return Result<SpreadsheetSheet>.Validation(
                        $"The file has more than {maxRows} rows. Split it into smaller files and import them separately.");

                var cells = new string[headers.Length];
                for (var i = 0; i < headers.Length; i++)
                    cells[i] = csv.TryGetField<string>(i, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

                // The physical line in the file, which is what the user's editor shows.
                rows.Add(new SpreadsheetRow { RowNumber = csv.Parser.Row, Cells = cells });
            }

            return Result<SpreadsheetSheet>.Success(new SpreadsheetSheet { Headers = headers, Rows = rows });
        }
        catch (CsvHelperException exception)
        {
            return Result<SpreadsheetSheet>.Validation($"The CSV file could not be read: {exception.Message}");
        }
    }

    private static Result<SpreadsheetSheet> ReadWorkbook(Stream content, int maxRows)
    {
        try
        {
            using var workbook = new XLWorkbook(content);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null)
                return Result<SpreadsheetSheet>.Validation("The workbook has no worksheets.");

            var usedRows = worksheet.RangeUsed()?.RowsUsed().ToList();
            if (usedRows is null || usedRows.Count == 0)
                return Result<SpreadsheetSheet>.Validation("The first worksheet is empty.");

            var headerCells = usedRows[0].Cells().ToList();
            var headers = NormalizeHeaders(headerCells.Select(CellText).ToArray());

            var rows = new List<SpreadsheetRow>();
            for (var index = 1; index < usedRows.Count; index++)
            {
                if (rows.Count >= maxRows)
                    return Result<SpreadsheetSheet>.Validation(
                        $"The workbook has more than {maxRows} rows. Split it into smaller files and import them separately.");

                var row = usedRows[index];
                var cells = new string[headers.Length];
                for (var column = 0; column < headers.Length; column++)
                    cells[column] = CellText(row.Cell(column + 1)).Trim();

                // The worksheet's own row number, so a reported problem points at the
                // row the user sees in Excel.
                rows.Add(new SpreadsheetRow { RowNumber = row.RowNumber(), Cells = cells });
            }

            return Result<SpreadsheetSheet>.Success(new SpreadsheetSheet { Headers = headers, Rows = rows });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<SpreadsheetSheet>.Validation(
                $"The workbook could not be read: {exception.Message}");
        }
    }

    private static string[] NormalizeHeaders(IReadOnlyList<string> headers) =>
        headers
            .Select((header, index) =>
            {
                var text = (header ?? string.Empty).Trim();
                return text.Length == 0 ? $"Column {index + 1}" : text;
            })
            .ToArray();

    /// <summary>
    /// One cell as text. A real date cell is rendered as ISO-8601 so the pipeline
    /// never has to guess a format, and a blank cell is empty text rather than a
    /// missing entry.
    /// </summary>
    private static string CellText(IXLCell cell) => cell.DataType switch
    {
        XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XLDataType.Blank => string.Empty,
        _ => cell.GetString()
    };
}
