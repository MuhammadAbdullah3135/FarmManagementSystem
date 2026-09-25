using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Infrastructure.Inventory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk customer import: read the file, map its columns, validate every row against
/// the same rules the create endpoint uses, then commit the batch atomically.
///
/// All of the mechanics live in <see cref="ImportPipeline"/>, shared with the animal,
/// employee, inventory and supplier importers. What is below is only what a customer is.
///
/// <para>
/// The relational rule is the name: it is the customer's identifier, the unique index on
/// (FarmId, Name) enforces it, and the create endpoint rejects a duplicate. So a duplicate
/// is an error here too — both against customers that already exist (via
/// <see cref="ICustomerService"/>) and against another row of the same file.
/// </para>
/// </summary>
public class CustomerImportService : ICustomerImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly ICustomerService _customers;
    private readonly ImportOptions _options;
    private readonly ILogger<CustomerImportService> _logger;

    public CustomerImportService(
        ISpreadsheetReader reader,
        ICustomerService customers,
        IOptions<ImportOptions> options,
        ILogger<CustomerImportService> logger)
    {
        _reader = reader;
        _customers = customers;
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

        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();
        if (locallyValid.Count > 0)
            await ApplyFarmValidationAsync(farmId, locallyValid);

        return Result<ImportPreviewDto>.Success(
            ImportPipeline.BuildPreview(analysis, CustomerImportFields.All, _options));
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
                "Refused customer import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so a customer added between the two calls is caught here and still
        // writes nothing.
        var creation = await _customers.CreateCustomersAsync(
            farmId, locallyValid.Select(row => row.Request!).ToList());

        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused customer import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} customers into farm {FarmId} from {FileName}",
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

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, CustomerImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, CustomerImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, CustomerImportFields.All);
        if (mappingError != null)
            return Result<Analysis>.Failure(mappingError);

        var firstRowByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var name = ImportPipeline.ReadValue(sheet.Rows[index], effective, CustomerImportFields.Name).Trim();
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

        ImportPipeline.ReadValues(spreadsheetRow, mapping, CustomerImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateCustomerRequest
        {
            Name = values[CustomerImportFields.Name].Trim(),
            ContactInfo = ImportPipeline.Trimmed(values[CustomerImportFields.ContactInfo])
        };

        // A name repeated inside the file is a file problem, so it is reported here with
        // both row numbers rather than left to the batch, which only knows positions.
        if (request.Name.Length > 0 &&
            firstRowByName.TryGetValue(request.Name, out var firstIndex) &&
            firstIndex != index)
        {
            errors.Add(ImportPipeline.FieldError(CustomerImportFields.Name,
                $"Duplicate customer name '{request.Name}' in this file: row {sheet.Rows[firstIndex].RowNumber} already uses it and is the one that will be imported."));
        }

        // The create endpoint's own rules, so a row cannot be accepted here where a
        // hand-entered customer would be rejected — or the other way round.
        foreach (var error in CustomerRules.Validate(request.Name, request.ContactInfo))
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
        var result = await _customers.ValidateCustomersAsync(farmId, rows.Select(row => row.Request!).ToList());

        if (!result.IsSuccess)
        {
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
    /// presentation only — the message itself is unchanged.
    /// </summary>
    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("A customer with this name", CustomerImportFields.Name),
        ("The batch contains the same customer name", CustomerImportFields.Name),
        ("Customer name is required", CustomerImportFields.Name),
        ("Customer name cannot exceed", CustomerImportFields.Name),
        ("Contact information cannot exceed", CustomerImportFields.ContactInfo),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        public CreateCustomerRequest? Request { get; set; }
    }
}
