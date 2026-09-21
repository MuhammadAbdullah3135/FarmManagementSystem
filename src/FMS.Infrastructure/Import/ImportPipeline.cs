using System.Globalization;
using FMS.Application.Common;
using FMS.Application.Import;

namespace FMS.Infrastructure.Import;

/// <summary>
/// Everything the import pipeline does that has nothing to do with the entity being
/// imported: header auto-detection, the mapping merge, reading a value from a column
/// or a constant, the never-guess date parser, per-row error assembly, the preview
/// and commit reports, and the by-name lookup resolution every importer needs.
///
/// This was lifted verbatim out of the animal importer, which had grown it privately.
/// It is shared as static helpers rather than a generic service on purpose: each
/// importer still owns its own vocabulary, lookups, row building and commit call, so
/// there is no reflection, no type parameter that means nothing, and no framework to
/// understand before reading one entity's rules.
///
/// <para>
/// One behaviour here is load-bearing and must not be "simplified": a date is never
/// guessed. An ambiguous <c>01/02/2023</c> is reported so the caller can set the
/// format, because silently picking one reading of it is how a herd's birth dates
/// end up wrong in a way nobody notices.
/// </para>
/// </summary>
public static class ImportPipeline
{
    // ── mapping ────────────────────────────────────────────

    /// <summary>What header auto-detection would choose for this file.</summary>
    public static ImportMapping SuggestMapping(IReadOnlyList<string> headers, IImportFieldCatalog fields)
    {
        var mapping = new ImportMapping();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < headers.Count; index++)
        {
            var field = fields.MatchHeader(headers[index]);
            if (field is null || !taken.Add(field))
                continue;

            mapping.Fields[field] = new ImportFieldMap { Column = index };
        }

        return mapping;
    }

    /// <summary>
    /// The mapping actually used: the caller's entries win for the fields it mentions,
    /// and auto-detection fills the rest. A field the caller mentions with an empty map
    /// is explicitly ignored, which is why presence — not truthiness — decides.
    /// </summary>
    public static ImportMapping Merge(
        ImportMapping? requested,
        ImportMapping suggested,
        IReadOnlyList<ImportFieldDescriptor> fields)
    {
        var effective = new ImportMapping
        {
            DateFormat = string.IsNullOrWhiteSpace(requested?.DateFormat) ? null : requested!.DateFormat!.Trim()
        };

        foreach (var field in fields)
        {
            if (requested is not null && requested.Fields.TryGetValue(field.Key, out var requestedMap))
                effective.Fields[field.Key] = requestedMap ?? new ImportFieldMap();
            else if (suggested.Fields.TryGetValue(field.Key, out var guessed))
                effective.Fields[field.Key] = guessed;
        }

        return effective;
    }

    public static Error? ValidateMapping(
        ImportMapping mapping,
        int headerCount,
        IReadOnlyList<ImportFieldDescriptor> fields)
    {
        foreach (var field in fields)
        {
            var map = mapping.For(field.Key);
            if (map?.Column is int column && (column < 0 || column >= headerCount))
                return Error.Validation(
                    $"'{field.Label}' is mapped to column {column + 1}, but the file has {headerCount} column(s).");
        }

        var unmapped = fields
            .Where(field => field.Required && !(mapping.For(field.Key)?.IsMapped ?? false))
            .Select(field => field.Label)
            .ToList();

        return unmapped.Count > 0
            ? Error.Validation($"{string.Join(", ", unmapped)} must be mapped to a column or a fixed value.")
            : null;
    }

    /// <summary>
    /// One field's raw text for a row: the mapped column's cell, or the constant when
    /// the field is mapped to a fixed value. Never throws for a short row — a missing
    /// cell reads as empty, which the field's own rule then reports.
    /// </summary>
    public static string ReadValue(SpreadsheetRow row, ImportMapping mapping, string fieldKey)
    {
        var map = mapping.For(fieldKey);
        if (map is null)
            return string.Empty;

        if (map.Column is int column)
            return column >= 0 && column < row.Cells.Count ? row.Cells[column] : string.Empty;

        return map.Constant ?? string.Empty;
    }

    /// <summary>Every field's raw text for one row, kept so the preview can show what was picked up.</summary>
    public static void ReadValues(
        SpreadsheetRow row,
        ImportMapping mapping,
        IReadOnlyList<ImportFieldDescriptor> fields,
        IDictionary<string, string> values)
    {
        foreach (var field in fields)
            values[field.Key] = ReadValue(row, mapping, field.Key);
    }

    // ── dates ──────────────────────────────────────────────

    private enum DateParseOutcome
    {
        Empty,
        Parsed,
        Invalid,
        Ambiguous
    }

    private static readonly string[] IsoDateFormats =
    {
        "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyy.MM.dd", "yyyy.M.d",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"
    };

    private static readonly string[] MonthNameDateFormats =
    {
        "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy",
        "MMM d yyyy", "MMM dd yyyy", "MMMM d yyyy", "MMMM dd yyyy"
    };

    /// <summary>Reads a date cell, reporting — never guessing — anything ambiguous.</summary>
    public static DateTime? ParseDateField(
        string raw,
        string fieldKey,
        string label,
        string? explicitFormat,
        List<ImportRowErrorDto> errors)
    {
        var (outcome, value) = ParseDate(raw, explicitFormat);

        switch (outcome)
        {
            case DateParseOutcome.Empty:
                return null;
            case DateParseOutcome.Parsed:
                return value;
            case DateParseOutcome.Ambiguous:
                errors.Add(FieldError(fieldKey,
                    $"{label} '{raw.Trim()}' is ambiguous. Write it as yyyy-MM-dd, or set the date format to dd/MM/yyyy or MM/dd/yyyy."));
                return null;
            default:
                errors.Add(FieldError(fieldKey,
                    $"{label} '{raw.Trim()}' is not a date. Use yyyy-MM-dd, or set the date format to match the file."));
                return null;
        }
    }

    /// <summary>
    /// Parses a date cell without ever guessing.
    ///
    /// ISO-8601 (and a real Excel date cell, which the reader already renders as ISO)
    /// is unambiguous, as is a month name. For everything else the components have to
    /// make the answer the only one possible — <c>15/04/2023</c> can only be April
    /// 15th, but <c>01/02/2023</c> is reported as ambiguous instead of being silently
    /// imported as one of two different dates. The caller resolves it by setting the
    /// import's date format.
    /// </summary>
    private static (DateParseOutcome Outcome, DateTime Value) ParseDate(string raw, string? explicitFormat)
    {
        var text = raw.Trim();
        if (text.Length == 0)
            return (DateParseOutcome.Empty, default);

        if (!string.IsNullOrWhiteSpace(explicitFormat))
        {
            return DateTime.TryParseExact(
                text, explicitFormat.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)
                ? (DateParseOutcome.Parsed, AsUtcDate(exact))
                : (DateParseOutcome.Invalid, default);
        }

        if (DateTime.TryParseExact(text, IsoDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return (DateParseOutcome.Parsed, AsUtcDate(iso));

        if (DateTime.TryParseExact(text, MonthNameDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var named))
            return (DateParseOutcome.Parsed, AsUtcDate(named));

        var parts = text.Split(new[] { '/', '.', '-' }, StringSplitOptions.TrimEntries);
        if (parts.Length == 3 &&
            parts[2].Length == 4 &&
            parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var second = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var year = int.Parse(parts[2], CultureInfo.InvariantCulture);

            if (first > 12 && second <= 12)
                return TryBuild(year, second, first);
            if (second > 12 && first <= 12)
                return TryBuild(year, first, second);
            if (first > 12 && second > 12)
                return (DateParseOutcome.Invalid, default);

            return (DateParseOutcome.Ambiguous, default);
        }

        return (DateParseOutcome.Invalid, default);
    }

    private static (DateParseOutcome Outcome, DateTime Value) TryBuild(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return (DateParseOutcome.Invalid, default);

        return (DateParseOutcome.Parsed, AsUtcDate(new DateTime(year, month, day)));
    }

    /// <summary>
    /// A date-only value marked as UTC. Npgsql refuses a non-UTC DateTime for a
    /// <c>timestamptz</c> column, and the create paths already store UTC.
    /// </summary>
    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    // ── numbers ────────────────────────────────────────────

    /// <summary>
    /// Reads a numeric cell without guessing at the wrong thing.
    ///
    /// A spreadsheet's own locale decides whether "12,5" means twelve and a half, so
    /// the reading is chosen by what the cell can only mean:
    /// <list type="number">
    /// <item>a decimal point is present, so it is the decimal separator and commas are
    /// thousands separators (<c>1,234.56</c>);</item>
    /// <item>no decimal point and exactly one comma with one or two digits after it, so
    /// the comma is the decimal separator (<c>12,5</c>);</item>
    /// <item>no decimal point otherwise, so commas are thousands separators
    /// (<c>1,234</c>) — the reading a three-digit group almost always means;</item>
    /// <item>anything else is a row error naming what was in the file.</item>
    /// </list>
    /// An empty cell is zero, which is what the create request's own default is.
    /// </summary>
    public static decimal ParseDecimalField(
        string raw,
        string fieldKey,
        string label,
        List<ImportRowErrorDto> errors)
    {
        var text = raw.Trim();
        if (text.Length == 0)
            return 0m;

        const NumberStyles Plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        const NumberStyles Grouped = Plain | NumberStyles.AllowThousands;

        if (text.Contains('.'))
        {
            if (decimal.TryParse(text, Grouped, CultureInfo.InvariantCulture, out var pointed))
                return pointed;
        }
        else if (text.Contains(','))
        {
            var afterLastComma = text[(text.LastIndexOf(',') + 1)..];
            var singleComma = text.Count(character => character == ',') == 1;

            var style = singleComma && afterLastComma.Length is 1 or 2 ? Plain : Grouped;
            var candidate = style == Plain ? text.Replace(',', '.') : text;

            if (decimal.TryParse(candidate, style, CultureInfo.InvariantCulture, out var comma))
                return comma;
        }
        else if (decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out var plain))
        {
            return plain;
        }

        errors.Add(FieldError(fieldKey, $"{label} '{text}' is not a number."));
        return 0m;
    }

    // ── lookups resolved by name ───────────────────────────

    /// <summary>A farm-scoped lookup option, with the parent that scopes it when it has one.</summary>
    public sealed record LookupEntry(Guid Id, string Name, Guid? ParentId);

    /// <summary>
    /// Resolves one cell against the farm's options by name.
    ///
    /// A name that matches nothing, or matches more than one option, is a row error
    /// that names what was in the file — the alternative, silently picking the first
    /// match, is how an import quietly attaches the wrong breed or department.
    /// </summary>
    public static LookupEntry? Resolve(
        IReadOnlyDictionary<string, List<LookupEntry>> byName,
        string raw,
        string fieldKey,
        string label,
        List<ImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (!byName.TryGetValue(ImportFieldCatalog.Normalize(value), out var matches))
        {
            errors.Add(FieldError(fieldKey, $"{label} '{value}' was not found in this farm"));
            return null;
        }

        if (matches.Count > 1)
        {
            errors.Add(FieldError(fieldKey, $"{label} '{value}' matches more than one record in this farm"));
            return null;
        }

        return matches[0];
    }

    /// <summary>Groups options by their normalized name, dropping the unnamed ones.</summary>
    public static Dictionary<string, List<LookupEntry>> GroupByName(IEnumerable<LookupEntry> entries)
    {
        var map = new Dictionary<string, List<LookupEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = ImportFieldCatalog.Normalize(entry.Name);
            if (key.Length == 0)
                continue;

            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<LookupEntry>();

            list.Add(entry);
        }

        return map;
    }

    /// <summary>The distinct, trimmed, non-blank option names, for the wizard's fixed-value picker.</summary>
    public static List<string> Names(IEnumerable<string> names) =>
        names.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── row errors and field attribution ───────────────────

    public static ImportRowErrorDto FieldError(string field, string message) =>
        new() { Field = field, Message = message };

    /// <summary>
    /// The catalog field a validator property name belongs to, so a rule that fires on
    /// a named property still points at a row of the mapping table. Falls back to the
    /// camel-cased property name, which is the convention every request DTO follows.
    /// </summary>
    public static string FieldKeyFor(string propertyName, IReadOnlyDictionary<string, string> map) =>
        map.TryGetValue(propertyName, out var key) ? key : CamelCase(propertyName);

    /// <summary>
    /// The catalog field a batch message belongs to.
    ///
    /// The batch reports a message, not a field, so the field is inferred from the
    /// message's own wording. This is presentation only — the message itself is
    /// unchanged, which is what keeps a row's error text identical to what the
    /// single-create endpoint would answer.
    /// </summary>
    public static string FieldKeyForMessage(string message, IReadOnlyList<(string Prefix, string Field)> prefixes)
    {
        foreach (var (prefix, field) in prefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return field;
        }

        return string.Empty;
    }

    private static string CamelCase(string value) =>
        value.Length == 0
            ? string.Empty
            : char.ToLowerInvariant(value[0]) + value[1..];

    // ── reports ────────────────────────────────────────────

    public static ImportRowDto ToRowDto(ImportRowAnalysis row) => new()
    {
        RowNumber = row.RowNumber,
        IsValid = row.Errors.Count == 0,
        Errors = row.Errors,
        Values = row.Values
    };

    public static ImportPreviewDto BuildPreview<TRow>(
        ImportAnalysis<TRow> analysis,
        IReadOnlyList<ImportFieldDescriptor> fields,
        ImportOptions options)
        where TRow : ImportRowAnalysis
    {
        var invalid = analysis.Rows.Where(row => row.Errors.Count > 0).ToList();
        var valid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();

        return new ImportPreviewDto
        {
            Fields = fields.ToList(),
            Headers = analysis.Sheet.Headers.ToList(),
            Mapping = analysis.Effective,
            SuggestedMapping = analysis.Suggested,
            Lookups = analysis.LookupNames,
            TotalRows = analysis.Rows.Count,
            ValidRowCount = valid.Count,
            InvalidRowCount = invalid.Count,
            Truncated = invalid.Count > options.MaxReportedRows,
            InvalidRows = invalid.Take(options.MaxReportedRows).Select(ToRowDto).ToList(),
            SampleValidRows = valid.Take(options.SampleValidRows).Select(ToRowDto).ToList()
        };
    }

    public static ImportCommitDto BuildCommit<TRow>(
        ImportAnalysis<TRow> analysis, ImportOptions options, int importedCount)
        where TRow : ImportRowAnalysis
    {
        var invalid = analysis.Rows.Where(row => row.Errors.Count > 0).ToList();

        return new ImportCommitDto
        {
            TotalRows = analysis.Rows.Count,
            ImportedCount = importedCount,
            Truncated = invalid.Count > options.MaxReportedRows,
            InvalidRows = invalid.Take(options.MaxReportedRows).Select(ToRowDto).ToList()
        };
    }
}

/// <summary>
/// One row of a file after mapping, with the raw text it was built from and every
/// problem found. Importers derive from this to carry their own parsed request, so the
/// shared reporting above works for any entity.
/// </summary>
public class ImportRowAnalysis
{
    public int RowNumber { get; init; }

    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public List<ImportRowErrorDto> Errors { get; } = new();
}

/// <summary>
/// A whole file's analysis: the parsed sheet, the mapping used and chosen, the farm's
/// lookup names, and one entry per data row.
///
/// Generic over the row type so an importer can attach its own parsed request to each
/// row while the reporting above still works on the shared base.
/// </summary>
public class ImportAnalysis<TRow>
    where TRow : ImportRowAnalysis
{
    public SpreadsheetSheet Sheet { get; init; } = null!;
    public ImportMapping Suggested { get; init; } = null!;
    public ImportMapping Effective { get; init; } = null!;
    public Dictionary<string, List<string>> LookupNames { get; init; } = new();
    public List<TRow> Rows { get; } = new();
}
