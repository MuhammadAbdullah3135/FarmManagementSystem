using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Application.Finance.Import;
using FMS.Application.Import;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk income import: the same pipeline as the expense importer, with the income
/// vocabulary (income categories, income date). Read the file, map its columns, resolve
/// the farm's income categories, payment methods, optional animal tags and locations,
/// validate every row against the create endpoint's rules, then commit the batch
/// atomically.
///
/// <para>
/// As with an expense, there is deliberately <b>no duplicate rule</b>: an income record
/// has no identifier and the create endpoint enforces none, so a heuristic here would
/// make a file stricter than the add form. The limitation is disclosed in the API docs and
/// the wizard.
/// </para>
/// </summary>
public class IncomeImportService : IIncomeImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IFinanceService _finance;
    private readonly FmsDbContext _context;
    private readonly ImportOptions _options;
    private readonly ILogger<IncomeImportService> _logger;

    public IncomeImportService(
        ISpreadsheetReader reader,
        IFinanceService finance,
        FmsDbContext context,
        IOptions<ImportOptions> options,
        ILogger<IncomeImportService> logger)
    {
        _reader = reader;
        _finance = finance;
        _context = context;
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
        var analysisResult = await AnalyseAsync(farmId, content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<ImportPreviewDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;

        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();
        if (locallyValid.Count > 0)
            await ApplyFarmValidationAsync(farmId, locallyValid);

        return Result<ImportPreviewDto>.Success(
            ImportPipeline.BuildPreview(analysis, IncomeImportFields.All, _options));
    }

    public async Task<Result<ImportCommitDto>> CommitAsync(
        Guid farmId,
        Stream content,
        string fileName,
        ImportMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyseAsync(farmId, content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<ImportCommitDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();

        if (locallyValid.Count != analysis.Rows.Count)
        {
            _logger.LogWarning(
                "Refused income import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so a category removed between the two calls is caught here and still
        // writes nothing.
        var creation = await _finance.CreateIncomeRecordsAsync(
            farmId, locallyValid.Select(row => row.Request!).ToList());

        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused income import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} income records into farm {FarmId} from {FileName}",
            creation.Value.SuccessCount, farmId, fileName);

        return Result<ImportCommitDto>.Success(
            ImportPipeline.BuildCommit(analysis, _options, creation.Value.SuccessCount));
    }

    private async Task<Result<Analysis>> AnalyseAsync(
        Guid farmId,
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

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, IncomeImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, IncomeImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, IncomeImportFields.All);
        if (mappingError != null)
            return Result<Analysis>.Failure(mappingError);

        var lookupResult = await LoadLookupsAsync(farmId, sheet, effective);
        if (!lookupResult.IsSuccess)
            return Result<Analysis>.Failure(lookupResult.Error!);

        var lookups = lookupResult.Value!;
        var analysis = new Analysis
        {
            Sheet = sheet,
            Suggested = suggested,
            Effective = effective,
            LookupNames = lookups.OptionNames
        };

        for (var index = 0; index < sheet.Rows.Count; index++)
            analysis.Rows.Add(BuildRow(sheet, index, lookups, effective));

        return Result<Analysis>.Success(analysis);
    }

    private static RowAnalysis BuildRow(
        SpreadsheetSheet sheet,
        int index,
        Lookups lookups,
        ImportMapping mapping)
    {
        var spreadsheetRow = sheet.Rows[index];
        var row = new RowAnalysis { RowNumber = spreadsheetRow.RowNumber };
        var errors = row.Errors;

        ImportPipeline.ReadValues(spreadsheetRow, mapping, IncomeImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateIncomeRecordRequest
        {
            Description = ImportPipeline.Trimmed(values[IncomeImportFields.Description])
        };

        request.IncomeDate = ImportPipeline.ParseDateField(
            values[IncomeImportFields.IncomeDate], IncomeImportFields.IncomeDate, "Income date",
            mapping.DateFormat, errors);
        request.Amount = ImportPipeline.ParseDecimalField(
            values[IncomeImportFields.Amount], IncomeImportFields.Amount, "Amount", errors);

        // The create endpoint's own rules, in its own order (amount, date, description).
        foreach (var error in IncomeRules.Validate(request.Amount, request.IncomeDate, request.Description))
        {
            if (errors.Any(existing => existing.Field == error.Field))
                continue;

            errors.Add(ImportPipeline.FieldError(error.Field, error.Message));
        }

        request.IncomeCategoryId = ImportPipeline.Resolve(
            lookups.IncomeCategories, values[IncomeImportFields.IncomeCategory],
            IncomeImportFields.IncomeCategory, "Income category", errors)?.Id ?? Guid.Empty;
        request.PaymentMethodId = ImportPipeline.Resolve(
            lookups.PaymentMethods, values[IncomeImportFields.PaymentMethod],
            IncomeImportFields.PaymentMethod, "Payment method", errors)?.Id ?? Guid.Empty;
        request.AnimalId = ResolveAnimal(
            values[IncomeImportFields.AnimalTag], IncomeImportFields.AnimalTag, "Animal tag", lookups.AnimalsByTag, errors);
        request.LocationId = ImportPipeline.Resolve(
            lookups.Locations, values[IncomeImportFields.Location],
            IncomeImportFields.Location, "Location", errors)?.Id;

        row.Request = request;
        return row;
    }

    private static Guid? ResolveAnimal(
        string raw,
        string fieldKey,
        string label,
        IReadOnlyDictionary<string, Guid> byTag,
        List<ImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (byTag.TryGetValue(value, out var id))
            return id;

        errors.Add(ImportPipeline.FieldError(fieldKey, $"{label} '{value}' was not found in this farm"));
        return null;
    }

    private async Task ApplyFarmValidationAsync(Guid farmId, List<RowAnalysis> rows)
    {
        var result = await _finance.ValidateIncomeRecordsAsync(farmId, rows.Select(row => row.Request!).ToList());

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

    private async Task<Result<Lookups>> LoadLookupsAsync(Guid farmId, SpreadsheetSheet sheet, ImportMapping mapping)
    {
        var categories = await _context.IncomeCategories
            .Where(category => category.FarmId == farmId)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync();

        var paymentMethods = await _context.PaymentMethods
            .Where(method => method.FarmId == farmId)
            .Select(method => new { method.Id, method.Name })
            .ToListAsync();

        var locations = await _context.Locations
            .Where(location => location.FarmId == farmId)
            .Select(location => new { location.Id, location.Name })
            .ToListAsync();

        var animalsByTag = await LoadAnimalTagsAsync(farmId, sheet, mapping);

        return Result<Lookups>.Success(new Lookups
        {
            IncomeCategories = ImportPipeline.GroupByName(
                categories.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            PaymentMethods = ImportPipeline.GroupByName(
                paymentMethods.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Locations = ImportPipeline.GroupByName(
                locations.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            AnimalsByTag = animalsByTag,
            OptionNames = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                [IncomeImportFields.IncomeCategory] = ImportPipeline.Names(categories.Select(item => item.Name)),
                [IncomeImportFields.PaymentMethod] = ImportPipeline.Names(paymentMethods.Select(item => item.Name)),
                [IncomeImportFields.Location] = ImportPipeline.Names(locations.Select(item => item.Name))
            }
        });
    }

    private async Task<IReadOnlyDictionary<string, Guid>> LoadAnimalTagsAsync(
        Guid farmId, SpreadsheetSheet sheet, ImportMapping mapping)
    {
        var candidateTags = sheet.Rows
            .Select(row => ImportPipeline.ReadValue(row, mapping, IncomeImportFields.AnimalTag).Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidateTags.Count == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var lowered = candidateTags.Select(tag => tag.ToLowerInvariant()).Distinct().ToList();
        var matches = await _context.Animals
            .Where(animal => animal.FarmId == farmId
                && !animal.IsDeleted
                && lowered.Contains(animal.TagNumber.ToLower()))
            .Select(animal => new { animal.TagNumber, animal.Id })
            .ToListAsync();

        return matches.ToDictionary(match => match.TagNumber, match => match.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("Amount must be greater than zero", IncomeImportFields.Amount),
        ("Income date cannot be in the future", IncomeImportFields.IncomeDate),
        ("Description cannot exceed", IncomeImportFields.Description),
        ("Income category not found", IncomeImportFields.IncomeCategory),
        ("Payment method not found", IncomeImportFields.PaymentMethod),
        ("Animal not found", IncomeImportFields.AnimalTag),
        ("Location not found", IncomeImportFields.Location),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        public CreateIncomeRecordRequest? Request { get; set; }
    }

    private sealed class Lookups
    {
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> IncomeCategories { get; init; } =
            new Dictionary<string, List<ImportPipeline.LookupEntry>>();

        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> PaymentMethods { get; init; } =
            new Dictionary<string, List<ImportPipeline.LookupEntry>>();

        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Locations { get; init; } =
            new Dictionary<string, List<ImportPipeline.LookupEntry>>();

        public IReadOnlyDictionary<string, Guid> AnimalsByTag { get; init; } =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> OptionNames { get; init; } = new();
    }
}
