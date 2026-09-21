using System.Text;
using ClosedXML.Excel;
using FMS.Infrastructure.Import;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The reader is the boundary between "whatever the farm exported" and the import
/// pipeline. It has to accept this app's own CSV export byte-for-byte, never throw on
/// a malformed file, and give both formats one normalized shape.
/// </summary>
public class SpreadsheetReaderTests
{
    private static readonly SpreadsheetReader Reader = new();

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ReadCsv_ParsesTheShapeTheAppsOwnExportProduces()
    {
        // client/src/utils/export.ts writes a UTF-8 BOM, "\n" line endings and quotes
        // only when a cell needs it. A file it produced has to import unchanged.
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(
                "Tag number,Name,Notes\n" +
                "TAG-0001,Bella,\"Says \"\"moo\"\", loudly\"\n" +
                "TAG-0002,Müller,\"Contains, a comma\"\n"))
            .ToArray();

        var result = await Reader.ReadAsync(new MemoryStream(bytes), "animals.csv", 100);

        Assert.True(result.IsSuccess);
        var sheet = result.Value!;
        Assert.Equal(new[] { "Tag number", "Name", "Notes" }, sheet.Headers);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(new[] { "TAG-0001", "Bella", "Says \"moo\", loudly" }, sheet.Rows[0].Cells);
        Assert.Equal("Müller", sheet.Rows[1].Cells[1]);
        Assert.Equal("Contains, a comma", sheet.Rows[1].Cells[2]);
    }

    [Fact]
    public async Task ReadCsv_ReportsTheLineNumberTheUserSees()
    {
        var result = await Reader.ReadAsync(Utf8("tagNumber\nTAG-1\n\nTAG-2\n"), "a.csv", 100);

        Assert.True(result.IsSuccess);
        // The blank line is skipped but still counted, so row numbers stay aligned with
        // the file the user is looking at.
        Assert.Equal(new[] { 2, 4 }, result.Value!.Rows.Select(row => row.RowNumber));
    }

    [Fact]
    public async Task ReadCsv_WithOnlyAHeaderRow_ReturnsNoRows()
    {
        var result = await Reader.ReadAsync(Utf8("tagNumber,name\n"), "a.csv", 100);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Rows);
    }

    [Fact]
    public async Task Read_AnEmptyFile_IsAValidationErrorRatherThanAnEmptySheet()
    {
        var result = await Reader.ReadAsync(Utf8(string.Empty), "a.csv", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task ReadCsv_PastTheRowLimit_IsAValidationError()
    {
        var result = await Reader.ReadAsync(Utf8("tagNumber\nA\nB\nC\n"), "a.csv", 2);

        Assert.False(result.IsSuccess);
        Assert.Contains("more than 2 rows", result.Error!.Message);
    }

    [Fact]
    public async Task ReadXlsx_ReadsARealDateCellAsIsoText()
    {
        // A genuine Excel date cell, which is what a farm's own spreadsheet will have.
        // The reader renders it as ISO-8601 so the pipeline never sees a bare serial
        // number and never has to guess a format.
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Animals");
        sheet.Cell(1, 1).Value = "tagNumber";
        sheet.Cell(1, 2).Value = "dateOfBirth";
        sheet.Cell(2, 1).Value = "TAG-1";
        sheet.Cell(2, 2).Value = new DateTime(2023, 4, 15);
        sheet.Cell(3, 1).Value = "TAG-2";
        sheet.Cell(3, 2).Value = new DateTime(2024, 2, 29);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = await Reader.ReadAsync(stream, "animals.xlsx", 100);

        Assert.True(result.IsSuccess);
        var parsed = result.Value!;
        Assert.Equal(new[] { "tagNumber", "dateOfBirth" }, parsed.Headers);
        Assert.Equal(new[] { "TAG-1", "2023-04-15" }, parsed.Rows[0].Cells);
        Assert.Equal(new[] { "TAG-2", "2024-02-29" }, parsed.Rows[1].Cells);
        // The worksheet's own row numbers, so a reported problem points at the row the
        // user sees in Excel.
        Assert.Equal(new[] { 2, 3 }, parsed.Rows.Select(row => row.RowNumber));
    }

    [Fact]
    public async Task ReadXlsx_WithABlankHeaderCell_NamesTheColumnPositionally()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(1, 1).Value = "tagNumber";
        sheet.Cell(1, 3).Value = "notes";
        sheet.Cell(2, 1).Value = "TAG-1";
        sheet.Cell(2, 3).Value = "fine";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = await Reader.ReadAsync(stream, "animals.xlsx", 100);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "tagNumber", "Column 2", "notes" }, result.Value!.Headers);
    }

    [Fact]
    public async Task ReadXlsx_ThatIsNotAWorkbook_IsAValidationErrorRatherThanAnException()
    {
        var result = await Reader.ReadAsync(Utf8("this is not a workbook"), "animals.xlsx", 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Theory]
    [InlineData("animals.xls", ".xls")]
    [InlineData("animals.pdf", ".pdf")]
    [InlineData("animals", "")]
    public async Task Read_AnUnsupportedFormat_NamesWhatIsAccepted(string fileName, string _)
    {
        var result = await Reader.ReadAsync(Utf8("tagNumber\nTAG-1\n"), fileName, 100);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Contains("CSV", result.Error!.Message);
    }

    [Fact]
    public async Task Read_LegacyXls_SaysToSaveAsXlsxOrCsv()
    {
        var result = await Reader.ReadAsync(Utf8("anything"), "animals.xls", 100);

        Assert.False(result.IsSuccess);
        Assert.Contains(".xlsx", result.Error!.Message);
    }
}
