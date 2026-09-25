using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using FMS.Application.Farm.Export;
using FMS.Infrastructure.Persistence;

namespace FMS.Infrastructure.Farm.Export;

/// <summary>
/// Writes a farm's whole dataset into one archive: a CSV per entity, plus a manifest
/// and a human-readable README.
///
/// <para>
/// <b>Why CSV in a ZIP rather than one multi-sheet workbook.</b> CSV is what the
/// importers already read, so each re-importable file is a format the app itself
/// consumes — re-importability is then a property of the format rather than a second
/// representation to keep in step. A ZIP also deflates as it goes: rows are enumerated
/// one at a time straight into the entry, so a farm with years of history costs a
/// streaming read rather than a workbook object graph in memory, which is what a
/// fifty-entity workbook would be.
/// </para>
///
/// <para>
/// The manifest is written last, deliberately: it carries each file's row count, and
/// those counts are only known once the files are written. Nothing is buffered to make
/// that possible — the ZIP is a streaming container, so entry order is free.
/// </para>
/// </summary>
public class FarmExportAssembler
{
    /// <summary>
    /// The same CSV byte shape the app's own browser export produces and its own reader
    /// accepts: UTF-8 with a BOM, <c>\n</c> line endings, and quotes only where a cell
    /// needs them. A file from this archive is therefore accepted back with no
    /// conversion step, which is what the round-trip test asserts.
    /// </summary>
    private static readonly CsvConfiguration Csv = new(CultureInfo.InvariantCulture)
    {
        NewLine = "\n"
    };

    private const string ManifestEntryName = "manifest.json";
    private const string ReadmeEntryName = "README.txt";

    /// <summary>Flush cadence while streaming rows; keeps the deflate buffer bounded.</summary>
    private const int FlushEveryRows = 500;

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    public const string ManifestFileName = ManifestEntryName;

    /// <summary>
    /// Writes the archive to <paramref name="destination"/> and returns the manifest it
    /// embedded.
    ///
    /// <paramref name="farmId"/> is the only source of scoping: every table is read with
    /// it, so no caller can widen the archive beyond one farm.
    /// </summary>
    public async Task<FarmExportManifestDto> WriteAsync(
        FmsDbContext db,
        Guid farmId,
        string farmName,
        Stream destination,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var manifest = new FarmExportManifestDto
        {
            FarmId = farmId,
            FarmName = farmName,
            GeneratedAtUtc = generatedAtUtc,
            ExcludedEntities = [.. FarmExportEntities.Exclusions],
            Notes = [.. FarmExportEntities.Notes]
        };

        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in FarmExportEntities.Tables)
            {
                var rowCount = await WriteTableAsync(zip, table, db, farmId, cancellationToken);

                manifest.Files.Add(new FarmExportFileDto
                {
                    FileName = table.FileName,
                    Entity = table.Entity,
                    RowCount = rowCount,
                    Columns = [.. table.Columns],
                    Reimportable = table.Reimportable
                });

                manifest.TotalRowCount += rowCount;
            }

            // Written after the data, because it carries each file's row count — and
            // without a BOM: the BOM rule belongs to the CSVs, which the app's reader
            // treats as a signature, while a JSON document is expected to start with JSON.
            WriteTextEntry(zip, ManifestEntryName, JsonSerializer.Serialize(manifest, ManifestJson), withBom: false);
            WriteTextEntry(zip, ReadmeEntryName, BuildReadme(manifest), withBom: true);
        }

        return manifest;
    }

    /// <summary>
    /// Streams one table into its own entry and returns how many data rows it wrote.
    ///
    /// The count is taken from the rows actually written rather than from a
    /// <c>Count()</c> query, so the manifest cannot claim more than the file holds — the
    /// two are the same pass.
    /// </summary>
    private static async Task<int> WriteTableAsync(
        ZipArchive zip,
        IFarmExportTable table,
        FmsDbContext db,
        Guid farmId,
        CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(table.FileName, CompressionLevel.Optimal);

        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(
            entryStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 16 * 1024,
            leaveOpen: true);
        await using var csv = new CsvWriter(writer, Csv);

        foreach (var column in table.Columns)
        {
            csv.WriteField(column);
        }

        await csv.NextRecordAsync();

        var rowCount = 0;

        await foreach (var row in table.ReadRowsAsync(db, farmId, cancellationToken))
        {
            for (var index = 0; index < table.Columns.Count; index++)
            {
                csv.WriteField(index < row.Length ? row[index] : string.Empty);
            }

            await csv.NextRecordAsync();
            rowCount++;

            if (rowCount % FlushEveryRows == 0)
            {
                await csv.FlushAsync();
                await writer.FlushAsync(cancellationToken);
            }
        }

        await csv.FlushAsync();
        await writer.FlushAsync(cancellationToken);

        return rowCount;
    }

    private static void WriteTextEntry(ZipArchive zip, string entryName, string content, bool withBom)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        writer.Write(content);
    }

    /// <summary>
    /// The archive's cover sheet: what is inside, what is not, and the caveats that would
    /// otherwise have to be discovered the hard way.
    /// </summary>
    private static string BuildReadme(FarmExportManifestDto manifest)
    {
        var text = new StringBuilder();

        text.AppendLine(manifest.Generator);
        text.AppendLine(new string('=', manifest.Generator.Length));
        text.AppendLine();
        text.AppendLine($"Farm:      {manifest.FarmName}");
        text.AppendLine($"Farm id:   {manifest.FarmId}");
        text.AppendLine($"Generated: {manifest.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        text.AppendLine($"Format:    {manifest.ArchiveFormat}");
        text.AppendLine($"Rows:      {manifest.TotalRowCount} across {manifest.Files.Count} files");
        text.AppendLine();
        text.AppendLine("Files");
        text.AppendLine("-----");

        foreach (var file in manifest.Files)
        {
            var marker = file.Reimportable
                ? "re-importable through its own import page"
                : "reference data";

            text.AppendLine($"  {file.FileName,-34} {file.RowCount,8} rows   {file.Entity} ({marker})");
        }

        text.AppendLine();
        text.AppendLine("Not included");
        text.AppendLine("------------");

        foreach (var exclusion in manifest.ExcludedEntities)
        {
            text.AppendLine($"  {exclusion.Entity}");
            text.AppendLine($"      {exclusion.Reason}");
        }

        text.AppendLine();
        text.AppendLine("Please note");
        text.AppendLine("-----------");

        foreach (var note in manifest.Notes)
        {
            text.AppendLine($"  - {note}");
        }

        text.AppendLine();
        text.AppendLine($"Each file's full column list is in {ManifestEntryName}.");

        return text.ToString();
    }
}
