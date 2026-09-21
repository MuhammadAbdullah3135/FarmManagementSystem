using System.Globalization;
using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Infrastructure.Inventory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk inventory import: read the file, map its columns, validate every row
/// against the same rules the create endpoint uses, then commit the batch atomically.
///
/// All of the mechanics — reading the sheet, header detection, the mapping merge,
/// value reading, number parsing, per-row reporting and the two reports — live in
/// <see cref="ImportPipeline"/>, shared with the animal and employee importers. What is
/// below is only what an inventory item is.
///
/// <para>
/// The relational rule is the name: it is the item's identifier, the unique index on
/// (FarmId, Name) enforces it, and the create endpoint rejects a duplicate. So a
/// duplicate is an error here too — never a silent skip and never an overwrite — both
/// against items that already exist (via <see cref="IInventoryService"/>, which owns
/// that check) and against another row of the same file (reported here, because only
/// this layer knows the row numbers).
/// </para>
/// </summary>
public class InventoryImportService : IInventoryImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IInventoryService _inventory;
    private readonly ImportOptions _options;
    private readonly ILogger<InventoryImportService> _logger;

    public InventoryImportService(
        ISpreadsheetReader reader,
        IInventoryService inventory,
        IOptions<ImportOptions> options,
        ILogger<InventoryImportService> logger)
    {
        _reader = reader;
        _inventory = inventory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ImportPreviewDto>> PreviewAsync(
        Guid farmId,
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyseAsync(content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<ImportPreviewDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;

        // Rows that passed the local checks are checked against the farm too, so one
        // preview lists everything wrong with the file.
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();
        if (locallyValid.Count > 0)
            await ApplyFarmValidationAsync(farmId, locallyValid);

        return Result<ImportPreviewDto>.Success(
            ImportPipeline.BuildPreview(analysis, InventoryImportFields.All, _options));
    }

    public async Task<Result<ImportCommitDto>> CommitAsync(
        Guid farmId,
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyseAsync(content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<ImportCommitDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();

        if (locallyValid.Count != analysis.Rows.Count)
        {
            _logger.LogWarning(
                "Refused inventory import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so an item created between the two calls is caught here and still
        // writes nothing.
        var creation = await _inventory.CreateItemsAsync(
            farmId, locallyValid.Select(row => row.Request!).ToList());

        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused inventory import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} inventory items into farm {FarmId} from {FileName}",
            creation.Value.SuccessCount, farmId, fileName);

        return Result<ImportCommitDto>.Success(
            ImportPipeline.BuildCommit(analysis, _options, creation.Value.SuccessCount));
    }

    private async Task<Result<Analysis>> AnalyseAsync(
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken)
    {
        var sheetResult = await _reader.ReadAsync(content, fileName, _options.MaxRows, cancellationToken);
        if (!sheetResult.IsSuccess)
            return Result<Analysis>.Failure(sheetResult.Error!);

        var sheet = sheetResult.Value!;
        if (sheet.Rows.Count == 0)
            return Result<Analysis>.Validation("The file has no data rows below the header row.");

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, InventoryImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, InventoryImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, InventoryImportFields.All);
        if (mappingError != null)
            return Result<Analysis>.Failure(mappingError);

        // First pass: which name first appears on which row, so a repeated name inside
        // the file can be reported against the later row and name the earlier one.
        var firstRowByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var name = ImportPipeline.ReadValue(sheet.Rows[index], effective, InventoryImportFields.Name).Trim();
            if (name.Length > 0 && !firstRowByName.ContainsKey(name))
                firstRowByName[name] = index;
        }

        var analysis = new Analysis { Sheet = sheet, Suggested = suggested, Effective = effective };

        for (var index = 0; index < sheet.Rows.Count; index++)
            analysis.Rows.Add(BuildRow(sheet, index, firstRowByName, effective));

        return Result<Analysis>.Success(analysis);
    }

    private static RowAnalysis BuildRow(
        SpreadsheetSheet sheet,
        int index,
        Dictionary<string, int> firstRowByName,
        ImportMapping mapping)
    {
        var spreadsheetRow = sheet.Rows[index];
        var row = new RowAnalysis { RowNumber = spreadsheetRow.RowNumber };
        var errors = row.Errors;

        ImportPipeline.ReadValues(spreadsheetRow, mapping, InventoryImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateInventoryItemRequest
        {
            Name = values[InventoryImportFields.Name].Trim(),
            Category = ImportPipeline.Trimmed(values[InventoryImportFields.Category]),
            Unit = values[InventoryImportFields.Unit].Trim(),
            Location = ImportPipeline.Trimmed(values[InventoryImportFields.Location])
        };

        // A name repeated inside the file is a file problem, so it is reported here with
        // both row numbers rather than left to the batch, which only knows positions.
        if (request.Name.Length > 0 &&
            firstRowByName.TryGetValue(request.Name, out var firstIndex) &&
            firstIndex != index)
        {
            errors.Add(ImportPipeline.FieldError(InventoryImportFields.Name,
                $"Duplicate item name '{request.Name}' in this file: row {sheet.Rows[firstIndex].RowNumber} already uses it and is the one that will be imported."));
        }

        request.Quantity = ImportPipeline.ParseDecimalField(
            values[InventoryImportFields.Quantity], InventoryImportFields.Quantity, "Quantity", errors);
        request.ReorderLevel = ImportPipeline.ParseDecimalField(
            values[InventoryImportFields.ReorderLevel], InventoryImportFields.ReorderLevel, "Reorder level", errors);
        request.UnitCost = ImportPipeline.ParseDecimalField(
            values[InventoryImportFields.UnitCost], InventoryImportFields.UnitCost, "Unit cost", errors);

        // The create endpoint's own rules, so a row cannot be accepted here where a
        // hand-entered item would be rejected — or the other way round.
        foreach (var error in InventoryItemRules.Validate(
            request.Name, request.Category, request.Unit, request.Quantity,
            request.ReorderLevel, request.UnitCost, request.Location))
        {
            if (errors.Any(existing => existing.Field == error.Field))
                continue;

            errors.Add(ImportPipeline.FieldError(error.Field, error.Message));
        }

        row.Request = request;
        return row;
    }

    private async Task ApplyFarmValidationAsync(Guid farmId, List<RowAnalysis> rows)
    {
        var result = await _inventory.ValidateItemsAsync(farmId, rows.Select(row => row.Request!).ToList());

        if (!result.IsSuccess)
        {
            // A batch-level refusal applies to the whole file.
            foreach (var row in rows)
                row.Errors.Add(new ImportRowErrorDto { Field = string.Empty, Message = result.Error!.Message });

            return;
        }

        ApplyFailures(rows, result.Value!.Failures);
    }

    private static void ApplyFailures(List<RowAnalysis> rows, IReadOnlyList<BulkCreateFailureDto> failures)
    {
        foreach (var failure in failures)
        {
            if (failure.Index < 0 || failure.Index >= rows.Count)
                continue;

            rows[failure.Index].Errors.Add(new ImportRowErrorDto
            {
                Field = ImportPipeline.FieldKeyForMessage(failure.Message, MessageFieldPrefixes),
                Message = failure.Message
            });
        }
    }

    /// <summary>
    /// The batch reports a message, not a field, so the field is inferred here. This is
    /// presentation only — the message itself is unchanged, which is what keeps a row's
    /// error text identical to what the create endpoint answers.
    /// </summary>
    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("An inventory item with this name", InventoryImportFields.Name),
        ("The batch contains the same item name", InventoryImportFields.Name),
        ("Item name is required", InventoryImportFields.Name),
        ("Item name cannot exceed", InventoryImportFields.Name),
        ("Unit is required", InventoryImportFields.Unit),
        ("Unit cannot exceed", InventoryImportFields.Unit),
        ("Quantity cannot be negative", InventoryImportFields.Quantity),
        ("Reorder level cannot be negative", InventoryImportFields.ReorderLevel),
        ("Unit cost cannot be negative", InventoryImportFields.UnitCost),
        ("Category cannot exceed", InventoryImportFields.Category),
        ("Location cannot exceed", InventoryImportFields.Location),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        public CreateInventoryItemRequest? Request { get; set; }
    }
}
