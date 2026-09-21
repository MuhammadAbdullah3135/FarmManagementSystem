using FMS.Application.Animal;
using FMS.Application.Animal.Import;
using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Application.Import;
using FMS.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk animal import: read the file, map its columns, resolve the farm's
/// lookups, validate every row, then commit the batch atomically.
///
/// Two rules shape the implementation:
///
/// 1. A row is validated with the same validator the single-create endpoint uses,
///    and the farm-level rules come from <see cref="IAnimalService"/> itself. There
///    is no second, more forgiving validation path for spreadsheets — what the
///    preview reports is what the commit does.
/// 2. Nothing is written until every row passes, and then everything is written in
///    one SaveChanges. A file is either imported or it is not.
///
/// Everything here that is not about animals — header detection, the mapping merge,
/// value reading, date parsing, per-row reporting and the by-name lookup resolution —
/// lives in <see cref="ImportPipeline"/>, shared with the employee and inventory
/// importers. What remains below is only what an animal actually is.
/// </summary>
public class AnimalImportService : IAnimalImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IConfigurationService _configuration;
    private readonly IAnimalService _animals;
    private readonly FmsDbContext _context;
    private readonly IValidator<CreateAnimalRequest> _validator;
    private readonly ImportOptions _options;
    private readonly ILogger<AnimalImportService> _logger;

    public AnimalImportService(
        ISpreadsheetReader reader,
        IConfigurationService configuration,
        IAnimalService animals,
        FmsDbContext context,
        IValidator<CreateAnimalRequest> validator,
        IOptions<ImportOptions> options,
        ILogger<AnimalImportService> logger)
    {
        _reader = reader;
        _configuration = configuration;
        _animals = animals;
        _context = context;
        _validator = validator;
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

        // Rows that passed the local checks are checked against the farm too, so a
        // preview lists everything wrong with the file in one pass.
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();
        if (locallyValid.Count > 0)
            await ApplyFarmValidationAsync(farmId, locallyValid);

        return Result<ImportPreviewDto>.Success(
            ImportPipeline.BuildPreview(analysis, AnimalImportFields.All, _options));
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
                "Refused animal import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        var items = locallyValid
            .Select(row => new BulkAnimalCreateItem { Id = row.Id, Request = row.Request! })
            .ToList();

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so a change to the farm between the two calls is caught here and
        // still writes nothing.
        var creation = await _animals.CreateAnimalsAsync(farmId, items);
        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused animal import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} animals into farm {FarmId} from {FileName}",
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

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, AnimalImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, AnimalImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, AnimalImportFields.All);
        if (mappingError != null)
            return Result<Analysis>.Failure(mappingError);

        var lookupsResult = await LoadLookupSetsAsync(farmId);
        if (!lookupsResult.IsSuccess)
            return Result<Analysis>.Failure(lookupsResult.Error!);

        var lookups = lookupsResult.Value!;
        var analysis = new Analysis
        {
            Sheet = sheet,
            Suggested = suggested,
            Effective = effective,
            LookupNames = lookups.OptionNames
        };

        // First pass: an id per row (so a row can name a parent created by the same
        // file) and the tags this file proposes.
        var rowIds = new List<Guid>(sheet.Rows.Count);
        var firstRowByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            rowIds.Add(Guid.NewGuid());

            var tag = ImportPipeline.ReadValue(sheet.Rows[index], effective, AnimalImportFields.TagNumber).Trim();
            if (tag.Length > 0 && !firstRowByTag.ContainsKey(tag))
                firstRowByTag[tag] = index;
        }

        var existingByTag = await LoadExistingTagIndexAsync(farmId, sheet, effective);

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            analysis.Rows.Add(BuildRow(
                sheet, index, rowIds, firstRowByTag, existingByTag, lookups, effective));
        }

        return Result<Analysis>.Success(analysis);
    }

    private RowAnalysis BuildRow(
        SpreadsheetSheet sheet,
        int index,
        List<Guid> rowIds,
        Dictionary<string, int> firstRowByTag,
        IReadOnlyDictionary<string, Guid> existingByTag,
        LookupSets lookups,
        ImportMapping mapping)
    {
        var spreadsheetRow = sheet.Rows[index];
        var row = new RowAnalysis { RowNumber = spreadsheetRow.RowNumber, Id = rowIds[index] };
        var errors = row.Errors;

        ImportPipeline.ReadValues(spreadsheetRow, mapping, AnimalImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateAnimalRequest { TagNumber = values[AnimalImportFields.TagNumber].Trim() };

        // A tag repeated inside the file is a file problem, so it is reported here with
        // both row numbers rather than left to the batch, which only knows positions.
        if (request.TagNumber.Length > 0 &&
            firstRowByTag.TryGetValue(request.TagNumber, out var firstIndex) &&
            firstIndex != index)
        {
            errors.Add(ImportPipeline.FieldError(AnimalImportFields.TagNumber,
                $"Duplicate tag '{request.TagNumber}' in this file: row {sheet.Rows[firstIndex].RowNumber} already uses it and is the one that will be imported."));
        }

        var animalType = ImportPipeline.Resolve(lookups.Types, values[AnimalImportFields.AnimalType],
            AnimalImportFields.AnimalType, "Animal type", errors);
        request.AnimalTypeId = animalType?.Id ?? Guid.Empty;

        var breedRaw = values[AnimalImportFields.Breed].Trim();
        if (breedRaw.Length > 0)
        {
            if (lookups.Breeds.TryGetValue(ImportFieldCatalog.Normalize(breedRaw), out var breedMatches))
            {
                // Breed names repeat across types, so the row's own animal type decides.
                var scoped = animalType is null
                    ? breedMatches
                    : breedMatches.Where(match => match.ParentId == animalType.Id).ToList();

                if (scoped.Count == 1)
                    request.BreedId = scoped[0].Id;
                else if (scoped.Count == 0)
                    errors.Add(ImportPipeline.FieldError(AnimalImportFields.Breed,
                        $"Breed '{breedRaw}' does not belong to animal type '{animalType?.Name ?? values[AnimalImportFields.AnimalType].Trim()}'"));
                else
                    errors.Add(ImportPipeline.FieldError(AnimalImportFields.Breed,
                        $"Breed '{breedRaw}' matches more than one breed in this farm"));
            }
            else
            {
                errors.Add(ImportPipeline.FieldError(AnimalImportFields.Breed, $"Breed '{breedRaw}' was not found in this farm"));
            }
        }

        // A blank required lookup is left to the validator, which reports it with the
        // same wording the create endpoint uses.
        request.SexOptionId = ImportPipeline.Resolve(lookups.Sexes, values[AnimalImportFields.Sex],
            AnimalImportFields.Sex, "Sex", errors)?.Id ?? Guid.Empty;
        request.AgeCategoryId = ImportPipeline.Resolve(lookups.AgeCategories, values[AnimalImportFields.AgeCategory],
            AnimalImportFields.AgeCategory, "Age category", errors)?.Id;
        request.AnimalStatusId = ImportPipeline.Resolve(lookups.Statuses, values[AnimalImportFields.Status],
            AnimalImportFields.Status, "Status", errors)?.Id ?? Guid.Empty;
        request.LocationId = ImportPipeline.Resolve(lookups.Locations, values[AnimalImportFields.Location],
            AnimalImportFields.Location, "Location", errors)?.Id;

        request.DateOfBirth = ImportPipeline.ParseDateField(
            values[AnimalImportFields.DateOfBirth], AnimalImportFields.DateOfBirth, "Date of birth", mapping.DateFormat, errors);
        request.AcquisitionDate = ImportPipeline.ParseDateField(
            values[AnimalImportFields.AcquisitionDate], AnimalImportFields.AcquisitionDate, "Acquisition date", mapping.DateFormat, errors);

        request.SireId = ResolveParent(
            values[AnimalImportFields.SireTag], AnimalImportFields.SireTag, "Sire tag",
            request.TagNumber, index, sheet, rowIds, firstRowByTag, existingByTag, errors);
        request.DamId = ResolveParent(
            values[AnimalImportFields.DamTag], AnimalImportFields.DamTag, "Dam tag",
            request.TagNumber, index, sheet, rowIds, firstRowByTag, existingByTag, errors);

        request.Name = ImportPipeline.Trimmed(values[AnimalImportFields.Name]);
        request.Notes = ImportPipeline.Trimmed(values[AnimalImportFields.Notes]);

        ApplyValidator(request, errors);

        row.Request = request;
        return row;
    }

    /// <summary>
    /// Reuses the exact validator the create endpoint runs, so an imported row cannot
    /// be accepted where a hand-entered animal would be rejected — or the other way
    /// round. Field names are translated to import field keys so the wizard can point
    /// at the right row of the mapping table.
    /// </summary>
    private void ApplyValidator(CreateAnimalRequest request, List<ImportRowErrorDto> errors)
    {
        var validation = _validator.Validate(request);
        if (validation.IsValid)
            return;

        foreach (var failure in validation.Errors)
        {
            var field = ImportPipeline.FieldKeyFor(failure.PropertyName, ValidatorFieldMap);

            // A value the row could not even resolve is already reported against this
            // field with a message naming what was in the file. Adding the validator's
            // "… is required" for a value we never managed to resolve would be noise.
            if (errors.Any(error => error.Field == field))
                continue;

            errors.Add(ImportPipeline.FieldError(field, failure.ErrorMessage));
        }
    }

    private async Task ApplyFarmValidationAsync(Guid farmId, List<RowAnalysis> rows)
    {
        var items = rows
            .Select(row => new BulkAnimalCreateItem { Id = row.Id, Request = row.Request! })
            .ToList();

        var result = await _animals.ValidateAnimalsAsync(farmId, items);
        if (!result.IsSuccess)
        {
            // A batch-level refusal applies to the whole file.
            foreach (var row in rows)
                row.Errors.Add(new ImportRowErrorDto { Field = string.Empty, Message = result.Error!.Message });

            return;
        }

        ApplyFailures(rows, result.Value!.Failures);
    }

    private static void ApplyFailures(List<RowAnalysis> rows, IReadOnlyList<BulkAnimalCreateFailureDto> failures)
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

    private async Task<Result<LookupSets>> LoadLookupSetsAsync(Guid farmId)
    {
        var types = await _configuration.GetAnimalTypesAsync(farmId);
        if (!types.IsSuccess) return Result<LookupSets>.Failure(types.Error!);

        var breeds = await _configuration.GetBreedsAsync(farmId);
        if (!breeds.IsSuccess) return Result<LookupSets>.Failure(breeds.Error!);

        var sexes = await _configuration.GetSexOptionsAsync(farmId);
        if (!sexes.IsSuccess) return Result<LookupSets>.Failure(sexes.Error!);

        var ageCategories = await _configuration.GetAgeCategoriesAsync(farmId);
        if (!ageCategories.IsSuccess) return Result<LookupSets>.Failure(ageCategories.Error!);

        var statuses = await _configuration.GetAnimalStatusesAsync(farmId);
        if (!statuses.IsSuccess) return Result<LookupSets>.Failure(statuses.Error!);

        var locations = await _configuration.GetLocationsAsync(farmId);
        if (!locations.IsSuccess) return Result<LookupSets>.Failure(locations.Error!);

        var flatLocations = Flatten(locations.Value!).ToList();

        return Result<LookupSets>.Success(new LookupSets
        {
            Types = ImportPipeline.GroupByName(types.Value!.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Breeds = ImportPipeline.GroupByName(breeds.Value!.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, item.AnimalTypeId))),
            Sexes = ImportPipeline.GroupByName(sexes.Value!.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Value, null))),
            AgeCategories = ImportPipeline.GroupByName(ageCategories.Value!.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Statuses = ImportPipeline.GroupByName(statuses.Value!.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Locations = ImportPipeline.GroupByName(flatLocations.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, item.ParentLocationId))),
            OptionNames = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                [AnimalImportFields.AnimalType] = ImportPipeline.Names(types.Value!.Select(item => item.Name)),
                [AnimalImportFields.Breed] = ImportPipeline.Names(breeds.Value!.Select(item => item.Name)),
                [AnimalImportFields.Sex] = ImportPipeline.Names(sexes.Value!.Select(item => item.Value)),
                [AnimalImportFields.AgeCategory] = ImportPipeline.Names(ageCategories.Value!.Select(item => item.Name)),
                [AnimalImportFields.Status] = ImportPipeline.Names(statuses.Value!.Select(item => item.Name)),
                [AnimalImportFields.Location] = ImportPipeline.Names(flatLocations.Select(item => item.Name)),
            }
        });
    }

    /// <summary>
    /// Resolves parent references by tag: an animal that already exists in the farm,
    /// or another row of this same file (which is why rows are given ids up front).
    /// Only the tags the file actually mentions are queried.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Guid>> LoadExistingTagIndexAsync(
        Guid farmId,
        SpreadsheetSheet sheet,
        ImportMapping mapping)
    {
        var candidateTags = sheet.Rows
            .SelectMany(row => new[]
            {
                ImportPipeline.ReadValue(row, mapping, AnimalImportFields.SireTag),
                ImportPipeline.ReadValue(row, mapping, AnimalImportFields.DamTag)
            })
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidateTags.Count == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Case-insensitive on purpose: a parent reference typed by hand should still
        // find the animal. Tag *uniqueness* stays exact, matching the create endpoint.
        var lowered = candidateTags.Select(tag => tag.ToLowerInvariant()).Distinct().ToList();
        var matches = await _context.Animals
            .Where(animal => animal.FarmId == farmId
                && !animal.IsDeleted
                && lowered.Contains(animal.TagNumber.ToLower()))
            .Select(animal => new { animal.TagNumber, animal.Id })
            .ToListAsync();

        return matches.ToDictionary(match => match.TagNumber, match => match.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static Guid? ResolveParent(
        string raw,
        string fieldKey,
        string label,
        string ownTag,
        int rowIndex,
        SpreadsheetSheet sheet,
        List<Guid> rowIds,
        Dictionary<string, int> firstRowByTag,
        IReadOnlyDictionary<string, Guid> existingByTag,
        List<ImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (ownTag.Length > 0 && string.Equals(value, ownTag, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(ImportPipeline.FieldError(fieldKey, $"{label} cannot be the animal's own tag"));
            return null;
        }

        if (existingByTag.TryGetValue(value, out var existingId))
            return existingId;

        if (firstRowByTag.TryGetValue(value, out var referencedIndex))
        {
            if (referencedIndex == rowIndex)
            {
                errors.Add(ImportPipeline.FieldError(fieldKey, $"{label} cannot be the animal's own tag"));
                return null;
            }

            return rowIds[referencedIndex];
        }

        errors.Add(ImportPipeline.FieldError(fieldKey, $"{label} '{value}' was not found in this farm or in this file"));
        return null;
    }

    private static IEnumerable<LocationDto> Flatten(IEnumerable<LocationDto> locations)
    {
        foreach (var location in locations)
        {
            yield return location;
            foreach (var child in Flatten(location.ChildLocations))
                yield return child;
        }
    }

    /// <summary>Validator property name to import field key.</summary>
    private static readonly Dictionary<string, string> ValidatorFieldMap = new(StringComparer.Ordinal)
    {
        ["TagNumber"] = AnimalImportFields.TagNumber,
        ["Name"] = AnimalImportFields.Name,
        ["AnimalTypeId"] = AnimalImportFields.AnimalType,
        ["SexOptionId"] = AnimalImportFields.Sex,
        ["AnimalStatusId"] = AnimalImportFields.Status,
        ["DateOfBirth"] = AnimalImportFields.DateOfBirth,
        ["AcquisitionDate"] = AnimalImportFields.AcquisitionDate,
        ["Notes"] = AnimalImportFields.Notes,
    };

    /// <summary>
    /// The batch reports a message, not a field, so the field is inferred here. This
    /// is presentation only — the message itself is unchanged, so a row's error text
    /// is identical to what the create endpoint would answer.
    /// </summary>
    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("An animal with tag", AnimalImportFields.TagNumber),
        ("Tag number is required", AnimalImportFields.TagNumber),
        ("Tag '", AnimalImportFields.TagNumber),
        ("The batch contains the same animal id", AnimalImportFields.TagNumber),
        ("Animal type not found", AnimalImportFields.AnimalType),
        ("Breed not found", AnimalImportFields.Breed),
        ("Sex option not found", AnimalImportFields.Sex),
        ("Age category not found", AnimalImportFields.AgeCategory),
        ("Animal status not found", AnimalImportFields.Status),
        ("Location not found", AnimalImportFields.Location),
        ("Sire animal not found", AnimalImportFields.SireTag),
        ("Dam animal not found", AnimalImportFields.DamTag),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        /// <summary>Id supplied to the batch, so rows in the file can reference each other.</summary>
        public Guid Id { get; init; }

        public CreateAnimalRequest? Request { get; set; }
    }

    private sealed class LookupSets
    {
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Types { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Breeds { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Sexes { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> AgeCategories { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Statuses { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Locations { get; init; } = new Dictionary<string, List<ImportPipeline.LookupEntry>>();
        public Dictionary<string, List<string>> OptionNames { get; init; } = new();
    }
}
