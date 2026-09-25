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
/// The bulk expense import: read the file, map its columns, resolve the farm's expense
/// categories, payment methods, optional animal tags and locations, validate every row
/// against the same rules the create endpoint uses, then commit the batch atomically.
///
/// All of the mechanics live in <see cref="ImportPipeline"/>. What is below is only what
/// an expense is.
///
/// <para>
/// There is deliberately <b>no duplicate rule</b>, unlike the inventory, employee,
/// supplier and customer importers. An expense has no identifier — no code column, and
/// two feed purchases of the same amount on the same day are not the same transaction —
/// and the create endpoint enforces no duplicate. Inventing one here (say date + amount +
/// category) would make a file stricter than the add form, which is the opposite of the
/// parity every other importer holds. The limitation is disclosed in the API docs and the
/// wizard instead.
/// </para>
/// </summary>
public class ExpenseImportService : IExpenseImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IFinanceService _finance;
    private readonly FmsDbContext _context;
    private readonly ImportOptions _options;
    private readonly ILogger<ExpenseImportService> _logger;

    public ExpenseImportService(
        ISpreadsheetReader reader,
        IFinanceService finance,
        FmsDbContext context,
        IOptions<ImportOptions> options,
        ILogger<ExpenseImportService> logger)
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
            ImportPipeline.BuildPreview(analysis, ExpenseImportFields.All, _options));
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
                "Refused expense import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so a category removed between the two calls is caught here and still
        // writes nothing.
        var creation = await _finance.CreateExpensesAsync(
            farmId, locallyValid.Select(row => row.Request!).ToList());

        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused expense import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} expenses into farm {FarmId} from {FileName}",
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

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, ExpenseImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, ExpenseImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, ExpenseImportFields.All);
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

        ImportPipeline.ReadValues(spreadsheetRow, mapping, ExpenseImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateExpenseRequest
        {
            Description = ImportPipeline.Trimmed(values[ExpenseImportFields.Description])
        };

        request.ExpenseDate = ImportPipeline.ParseDateField(
            values[ExpenseImportFields.ExpenseDate], ExpenseImportFields.ExpenseDate, "Expense date",
            mapping.DateFormat, errors);
        request.Amount = ImportPipeline.ParseDecimalField(
            values[ExpenseImportFields.Amount], ExpenseImportFields.Amount, "Amount", errors);

        // The create endpoint's own rules, in its own order (amount, date, description),
        // so a row cannot be accepted here where a hand-entered expense would be rejected
        // — or the other way round.
        foreach (var error in ExpenseRules.Validate(request.Amount, request.ExpenseDate, request.Description))
        {
            if (errors.Any(existing => existing.Field == error.Field))
                continue;

            errors.Add(ImportPipeline.FieldError(error.Field, error.Message));
        }

        request.ExpenseCategoryId = ImportPipeline.Resolve(
            lookups.ExpenseCategories, values[ExpenseImportFields.ExpenseCategory],
            ExpenseImportFields.ExpenseCategory, "Expense category", errors)?.Id ?? Guid.Empty;
        request.PaymentMethodId = ImportPipeline.Resolve(
            lookups.PaymentMethods, values[ExpenseImportFields.PaymentMethod],
            ExpenseImportFields.PaymentMethod, "Payment method", errors)?.Id ?? Guid.Empty;
        request.AnimalId = ResolveAnimal(
            values[ExpenseImportFields.AnimalTag], ExpenseImportFields.AnimalTag, "Animal tag", lookups.AnimalsByTag, errors);
        request.LocationId = ImportPipeline.Resolve(
            lookups.Locations, values[ExpenseImportFields.Location],
            ExpenseImportFields.Location, "Location", errors)?.Id;

        row.Request = request;
        return row;
    }

    /// <summary>
    /// Resolves the optional animal reference by tag. A tag that matches nothing is a row
    /// error naming what was in the file, the same shape the other lookups use.
    /// </summary>
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
        var result = await _finance.ValidateExpensesAsync(farmId, rows.Select(row => row.Request!).ToList());

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
    /// The farm's expense categories, payment methods and locations by name, plus the
    /// animal tags this file actually references. The names are also handed to the wizard
    /// so a "fixed value" mapping offers real options.
    /// </summary>
    private async Task<Result<Lookups>> LoadLookupsAsync(Guid farmId, SpreadsheetSheet sheet, ImportMapping mapping)
    {
        var categories = await _context.ExpenseCategories
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
            ExpenseCategories = ImportPipeline.GroupByName(
                categories.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            PaymentMethods = ImportPipeline.GroupByName(
                paymentMethods.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Locations = ImportPipeline.GroupByName(
                locations.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            AnimalsByTag = animalsByTag,
            OptionNames = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                [ExpenseImportFields.ExpenseCategory] = ImportPipeline.Names(categories.Select(item => item.Name)),
                [ExpenseImportFields.PaymentMethod] = ImportPipeline.Names(paymentMethods.Select(item => item.Name)),
                [ExpenseImportFields.Location] = ImportPipeline.Names(locations.Select(item => item.Name))
            }
        });
    }

    /// <summary>
    /// Only the animal tags the file mentions are looked up, case-insensitively — a tag
    /// typed by hand should still find the animal, matching the animal importer's parent
    /// references.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Guid>> LoadAnimalTagsAsync(
        Guid farmId, SpreadsheetSheet sheet, ImportMapping mapping)
    {
        var candidateTags = sheet.Rows
            .Select(row => ImportPipeline.ReadValue(row, mapping, ExpenseImportFields.AnimalTag).Trim())
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

    /// <summary>
    /// The batch reports a message, not a field, so the field is inferred here. This is
    /// presentation only — the message itself is unchanged, which is what keeps a row's
    /// error text identical to what the create endpoint answers.
    /// </summary>
    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("Amount must be greater than zero", ExpenseImportFields.Amount),
        ("Expense date cannot be in the future", ExpenseImportFields.ExpenseDate),
        ("Description cannot exceed", ExpenseImportFields.Description),
        ("Expense category not found", ExpenseImportFields.ExpenseCategory),
        ("Payment method not found", ExpenseImportFields.PaymentMethod),
        ("Animal not found", ExpenseImportFields.AnimalTag),
        ("Location not found", ExpenseImportFields.Location),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        public CreateExpenseRequest? Request { get; set; }
    }

    private sealed class Lookups
    {
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> ExpenseCategories { get; init; } =
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
