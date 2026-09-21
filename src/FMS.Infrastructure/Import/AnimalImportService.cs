using System.Globalization;
using System.Text.Json;
using FMS.Application.Animal;
using FMS.Application.Animal.Import;
using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk animal import pipeline: read the file, map its columns, resolve the
/// farm's lookups, validate every row, then commit the batch atomically.
///
/// Two rules shape the implementation:
///
/// 1. A row is validated with the same validator the single-create endpoint uses,
///    and the farm-level rules come from <see cref="IAnimalService"/> itself. There
///    is no second, more forgiving validation path for spreadsheets — what the
///    preview reports is what the commit does.
/// 2. Nothing is written until every row passes, and then everything is written in
///    one SaveChanges. A file is either imported or it is not.
/// </summary>
public class AnimalImportService : IAnimalImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IConfigurationService _configuration;
    private readonly IAnimalService _animals;
    private readonly FmsDbContext _context;
    private readonly IValidator<CreateAnimalRequest> _validator;
    private readonly AnimalImportOptions _options;
    private readonly ILogger<AnimalImportService> _logger;

    public AnimalImportService(
        ISpreadsheetReader reader,
        IConfigurationService configuration,
        IAnimalService animals,
        FmsDbContext context,
        IValidator<CreateAnimalRequest> validator,
        IOptions<AnimalImportOptions> options,
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

    public async Task<Result<AnimalImportPreviewDto>> PreviewAsync(
        Guid farmId,
        Stream content,
        string fileName,
        AnimalImportMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyseAsync(farmId, content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<AnimalImportPreviewDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;

        // Rows that passed the local checks are checked against the farm too, so a
        // preview lists everything wrong with the file in one pass.
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();
        if (locallyValid.Count > 0)
            await ApplyFarmValidationAsync(farmId, locallyValid);

        return Result<AnimalImportPreviewDto>.Success(BuildPreview(analysis));
    }

    public async Task<Result<AnimalImportCommitDto>> CommitAsync(
        Guid farmId,
        Stream content,
        string fileName,
        AnimalImportMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyseAsync(farmId, content, fileName, mapping, cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result<AnimalImportCommitDto>.Failure(analysisResult.Error!);

        var analysis = analysisResult.Value!;
        var locallyValid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();

        if (locallyValid.Count != analysis.Rows.Count)
        {
            _logger.LogWarning(
                "Refused animal import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<AnimalImportCommitDto>.Success(BuildCommit(analysis, importedCount: 0));
        }

        var items = locallyValid
            .Select(row => new BulkAnimalCreateItem { Id = row.Id, Request = row.Request! })
            .ToList();

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so a change to the farm between the two calls is caught here and
        // still writes nothing.
        var creation = await _animals.CreateAnimalsAsync(farmId, items);
        if (!creation.IsSuccess)
            return Result<AnimalImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused animal import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<AnimalImportCommitDto>.Success(BuildCommit(analysis, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} animals into farm {FarmId} from {FileName}",
            creation.Value.SuccessCount, farmId, fileName);

        return Result<AnimalImportCommitDto>.Success(BuildCommit(analysis, importedCount: creation.Value.SuccessCount));
    }

    private async Task<Result<Analysis>> AnalyseAsync(
        Guid farmId,
        Stream content,
        string fileName,
        AnimalImportMapping? mapping,
        CancellationToken cancellationToken)
    {
        var sheetResult = await _reader.ReadAsync(content, fileName, _options.MaxRows, cancellationToken);
        if (!sheetResult.IsSuccess)
            return Result<Analysis>.Failure(sheetResult.Error!);

        var sheet = sheetResult.Value!;
        if (sheet.Rows.Count == 0)
            return Result<Analysis>.Validation("The file has no data rows below the header row.");

        var suggested = SuggestMapping(sheet.Headers);
        var effective = Merge(mapping, suggested);

        var mappingError = ValidateMapping(effective, sheet.Headers.Count);
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

            var tag = ReadValue(sheet.Rows[index], effective, AnimalImportFields.TagNumber).Trim();
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
        AnimalImportMapping mapping)
    {
        var spreadsheetRow = sheet.Rows[index];
        var row = new RowAnalysis { RowNumber = spreadsheetRow.RowNumber, Id = rowIds[index] };
        var errors = row.Errors;

        foreach (var field in AnimalImportFields.All)
            row.Values[field.Key] = ReadValue(spreadsheetRow, mapping, field.Key);

        var values = row.Values;
        var request = new CreateAnimalRequest { TagNumber = values[AnimalImportFields.TagNumber].Trim() };

        // A tag repeated inside the file is a file problem, so it is reported here with
        // both row numbers rather than left to the batch, which only knows positions.
        if (request.TagNumber.Length > 0 &&
            firstRowByTag.TryGetValue(request.TagNumber, out var firstIndex) &&
            firstIndex != index)
        {
            errors.Add(FieldError(AnimalImportFields.TagNumber,
                $"Duplicate tag '{request.TagNumber}' in this file: row {sheet.Rows[firstIndex].RowNumber} already uses it and is the one that will be imported."));
        }

        var animalType = Resolve(lookups.Types, values[AnimalImportFields.AnimalType],
            AnimalImportFields.AnimalType, "Animal type", errors);
        request.AnimalTypeId = animalType?.Id ?? Guid.Empty;

        var breedRaw = values[AnimalImportFields.Breed].Trim();
        if (breedRaw.Length > 0)
        {
            if (lookups.Breeds.TryGetValue(AnimalImportFields.Normalize(breedRaw), out var breedMatches))
            {
                // Breed names repeat across types, so the row's own animal type decides.
                var scoped = animalType is null
                    ? breedMatches
                    : breedMatches.Where(match => match.ParentId == animalType.Id).ToList();

                if (scoped.Count == 1)
                    request.BreedId = scoped[0].Id;
                else if (scoped.Count == 0)
                    errors.Add(FieldError(AnimalImportFields.Breed,
                        $"Breed '{breedRaw}' does not belong to animal type '{animalType?.Name ?? values[AnimalImportFields.AnimalType].Trim()}'"));
                else
                    errors.Add(FieldError(AnimalImportFields.Breed,
                        $"Breed '{breedRaw}' matches more than one breed in this farm"));
            }
            else
            {
                errors.Add(FieldError(AnimalImportFields.Breed, $"Breed '{breedRaw}' was not found in this farm"));
            }
        }

        // A blank required lookup is left to the validator, which reports it with the
        // same wording the create endpoint uses.
        request.SexOptionId = Resolve(lookups.Sexes, values[AnimalImportFields.Sex],
            AnimalImportFields.Sex, "Sex", errors)?.Id ?? Guid.Empty;
        request.AgeCategoryId = Resolve(lookups.AgeCategories, values[AnimalImportFields.AgeCategory],
            AnimalImportFields.AgeCategory, "Age category", errors)?.Id;
        request.AnimalStatusId = Resolve(lookups.Statuses, values[AnimalImportFields.Status],
            AnimalImportFields.Status, "Status", errors)?.Id ?? Guid.Empty;
        request.LocationId = Resolve(lookups.Locations, values[AnimalImportFields.Location],
            AnimalImportFields.Location, "Location", errors)?.Id;

        request.DateOfBirth = ParseDateField(
            values[AnimalImportFields.DateOfBirth], AnimalImportFields.DateOfBirth, "Date of birth", mapping.DateFormat, errors);
        request.AcquisitionDate = ParseDateField(
            values[AnimalImportFields.AcquisitionDate], AnimalImportFields.AcquisitionDate, "Acquisition date", mapping.DateFormat, errors);

        request.SireId = ResolveParent(
            values[AnimalImportFields.SireTag], AnimalImportFields.SireTag, "Sire tag",
            request.TagNumber, index, sheet, rowIds, firstRowByTag, existingByTag, errors);
        request.DamId = ResolveParent(
            values[AnimalImportFields.DamTag], AnimalImportFields.DamTag, "Dam tag",
            request.TagNumber, index, sheet, rowIds, firstRowByTag, existingByTag, errors);

        request.Name = Trimmed(values[AnimalImportFields.Name]);
        request.Notes = Trimmed(values[AnimalImportFields.Notes]);

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
    private void ApplyValidator(CreateAnimalRequest request, List<AnimalImportRowErrorDto> errors)
    {
        var validation = _validator.Validate(request);
        if (validation.IsValid)
            return;

        foreach (var failure in validation.Errors)
        {
            var field = FieldKeyFor(failure.PropertyName);

            // A value the row could not even resolve is already reported against this
            // field with a message naming what was in the file. Adding the validator's
            // "… is required" for a value we never managed to resolve would be noise.
            if (errors.Any(error => error.Field == field))
                continue;

            errors.Add(new AnimalImportRowErrorDto { Field = field, Message = failure.ErrorMessage });
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
                row.Errors.Add(new AnimalImportRowErrorDto { Field = string.Empty, Message = result.Error!.Message });

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

            rows[failure.Index].Errors.Add(new AnimalImportRowErrorDto
            {
                Field = FieldKeyForMessage(failure.Message),
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
            Types = Group(types.Value!.Select(item => new LookupEntry(item.Id, item.Name, null))),
            Breeds = Group(breeds.Value!.Select(item => new LookupEntry(item.Id, item.Name, item.AnimalTypeId))),
            Sexes = Group(sexes.Value!.Select(item => new LookupEntry(item.Id, item.Value, null))),
            AgeCategories = Group(ageCategories.Value!.Select(item => new LookupEntry(item.Id, item.Name, null))),
            Statuses = Group(statuses.Value!.Select(item => new LookupEntry(item.Id, item.Name, null))),
            Locations = Group(flatLocations.Select(item => new LookupEntry(item.Id, item.Name, item.ParentLocationId))),
            OptionNames = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                [AnimalImportFields.AnimalType] = Names(types.Value!.Select(item => item.Name)),
                [AnimalImportFields.Breed] = Names(breeds.Value!.Select(item => item.Name)),
                [AnimalImportFields.Sex] = Names(sexes.Value!.Select(item => item.Value)),
                [AnimalImportFields.AgeCategory] = Names(ageCategories.Value!.Select(item => item.Name)),
                [AnimalImportFields.Status] = Names(statuses.Value!.Select(item => item.Name)),
                [AnimalImportFields.Location] = Names(flatLocations.Select(item => item.Name)),
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
        AnimalImportMapping mapping)
    {
        var candidateTags = sheet.Rows
            .SelectMany(row => new[]
            {
                ReadValue(row, mapping, AnimalImportFields.SireTag),
                ReadValue(row, mapping, AnimalImportFields.DamTag)
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
        List<AnimalImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (ownTag.Length > 0 && string.Equals(value, ownTag, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(FieldError(fieldKey, $"{label} cannot be the animal's own tag"));
            return null;
        }

        if (existingByTag.TryGetValue(value, out var existingId))
            return existingId;

        if (firstRowByTag.TryGetValue(value, out var referencedIndex))
        {
            if (referencedIndex == rowIndex)
            {
                errors.Add(FieldError(fieldKey, $"{label} cannot be the animal's own tag"));
                return null;
            }

            return rowIds[referencedIndex];
        }

        errors.Add(FieldError(fieldKey, $"{label} '{value}' was not found in this farm or in this file"));
        return null;
    }

    private static LookupEntry? Resolve(
        IReadOnlyDictionary<string, List<LookupEntry>> byName,
        string raw,
        string fieldKey,
        string label,
        List<AnimalImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (!byName.TryGetValue(AnimalImportFields.Normalize(value), out var matches))
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

    private static DateTime? ParseDateField(
        string raw,
        string fieldKey,
        string label,
        string? explicitFormat,
        List<AnimalImportRowErrorDto> errors)
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

    /// <summary>
    /// Parses a date cell without ever guessing.
    ///
    /// ISO-8601 (and a real Excel date cell, which the reader already renders as
    /// ISO) is unambiguous, as is a month name. For everything else the components
    /// have to make the answer the only one possible — <c>15/04/2023</c> can only be
    /// April 15th, but <c>01/02/2023</c> is reported as ambiguous instead of being
    /// silently imported as one of two different dates. The caller resolves it by
    /// setting the import's date format.
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
    /// <c>timestamptz</c> column, and the create path already stores UTC.
    /// </summary>
    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static AnimalImportMapping SuggestMapping(IReadOnlyList<string> headers)
    {
        var mapping = new AnimalImportMapping();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < headers.Count; index++)
        {
            var field = AnimalImportFields.MatchHeader(headers[index]);
            if (field is null || !taken.Add(field))
                continue;

            mapping.Fields[field] = new AnimalImportFieldMap { Column = index };
        }

        return mapping;
    }

    /// <summary>
    /// The mapping actually used: the caller's entries win for the fields it mentions,
    /// and auto-detection fills the rest. A field the caller mentions with an empty map
    /// is explicitly ignored, which is why presence — not truthiness — decides.
    /// </summary>
    private static AnimalImportMapping Merge(AnimalImportMapping? requested, AnimalImportMapping suggested)
    {
        var effective = new AnimalImportMapping
        {
            DateFormat = string.IsNullOrWhiteSpace(requested?.DateFormat) ? null : requested!.DateFormat!.Trim()
        };

        foreach (var field in AnimalImportFields.All)
        {
            if (requested is not null && requested.Fields.TryGetValue(field.Key, out var requestedMap))
                effective.Fields[field.Key] = requestedMap ?? new AnimalImportFieldMap();
            else if (suggested.Fields.TryGetValue(field.Key, out var guessed))
                effective.Fields[field.Key] = guessed;
        }

        return effective;
    }

    private static Error? ValidateMapping(AnimalImportMapping mapping, int headerCount)
    {
        foreach (var field in AnimalImportFields.All)
        {
            var map = mapping.For(field.Key);
            if (map?.Column is int column && (column < 0 || column >= headerCount))
                return Error.Validation(
                    $"'{field.Label}' is mapped to column {column + 1}, but the file has {headerCount} column(s).");
        }

        var unmapped = AnimalImportFields.All
            .Where(field => field.Required && !(mapping.For(field.Key)?.IsMapped ?? false))
            .Select(field => field.Label)
            .ToList();

        return unmapped.Count > 0
            ? Error.Validation($"{string.Join(", ", unmapped)} must be mapped to a column or a fixed value.")
            : null;
    }

    private static string ReadValue(SpreadsheetRow row, AnimalImportMapping mapping, string fieldKey)
    {
        var map = mapping.For(fieldKey);
        if (map is null)
            return string.Empty;

        if (map.Column is int column)
            return column >= 0 && column < row.Cells.Count ? row.Cells[column] : string.Empty;

        return map.Constant ?? string.Empty;
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

    private static Dictionary<string, List<LookupEntry>> Group(IEnumerable<LookupEntry> entries)
    {
        var map = new Dictionary<string, List<LookupEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = AnimalImportFields.Normalize(entry.Name);
            if (key.Length == 0)
                continue;

            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<LookupEntry>();

            list.Add(entry);
        }

        return map;
    }

    private static List<string> Names(IEnumerable<string> names) =>
        names.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AnimalImportRowErrorDto FieldError(string field, string message) =>
        new() { Field = field, Message = message };

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

    private static string FieldKeyForMessage(string message)
    {
        foreach (var (prefix, field) in MessageFieldPrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return field;
        }

        return string.Empty;
    }

    private static string FieldKeyFor(string propertyName) =>
        ValidatorFieldMap.TryGetValue(propertyName, out var key)
            ? key
            : JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private AnimalImportPreviewDto BuildPreview(Analysis analysis)
    {
        var invalid = analysis.Rows.Where(row => row.Errors.Count > 0).ToList();
        var valid = analysis.Rows.Where(row => row.Errors.Count == 0).ToList();

        return new AnimalImportPreviewDto
        {
            Fields = AnimalImportFields.All.ToList(),
            Headers = analysis.Sheet.Headers.ToList(),
            Mapping = analysis.Effective,
            SuggestedMapping = analysis.Suggested,
            Lookups = analysis.LookupNames,
            TotalRows = analysis.Rows.Count,
            ValidRowCount = valid.Count,
            InvalidRowCount = invalid.Count,
            Truncated = invalid.Count > _options.MaxReportedRows,
            InvalidRows = invalid.Take(_options.MaxReportedRows).Select(ToRowDto).ToList(),
            SampleValidRows = valid.Take(_options.SampleValidRows).Select(ToRowDto).ToList()
        };
    }

    private AnimalImportCommitDto BuildCommit(Analysis analysis, int importedCount)
    {
        var invalid = analysis.Rows.Where(row => row.Errors.Count > 0).ToList();

        return new AnimalImportCommitDto
        {
            TotalRows = analysis.Rows.Count,
            ImportedCount = importedCount,
            Truncated = invalid.Count > _options.MaxReportedRows,
            InvalidRows = invalid.Take(_options.MaxReportedRows).Select(ToRowDto).ToList()
        };
    }

    private static AnimalImportRowDto ToRowDto(RowAnalysis row) => new()
    {
        RowNumber = row.RowNumber,
        IsValid = row.Errors.Count == 0,
        Errors = row.Errors,
        Values = row.Values
    };

    private sealed class Analysis
    {
        public SpreadsheetSheet Sheet { get; init; } = null!;
        public AnimalImportMapping Suggested { get; init; } = null!;
        public AnimalImportMapping Effective { get; init; } = null!;
        public Dictionary<string, List<string>> LookupNames { get; init; } = new();
        public List<RowAnalysis> Rows { get; } = new();
    }

    private sealed class RowAnalysis
    {
        public int RowNumber { get; init; }
        public Guid Id { get; init; }
        public CreateAnimalRequest? Request { get; set; }
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public List<AnimalImportRowErrorDto> Errors { get; } = new();
    }

    private sealed record LookupEntry(Guid Id, string Name, Guid? ParentId);

    private sealed class LookupSets
    {
        public IReadOnlyDictionary<string, List<LookupEntry>> Types { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public IReadOnlyDictionary<string, List<LookupEntry>> Breeds { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public IReadOnlyDictionary<string, List<LookupEntry>> Sexes { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public IReadOnlyDictionary<string, List<LookupEntry>> AgeCategories { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public IReadOnlyDictionary<string, List<LookupEntry>> Statuses { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public IReadOnlyDictionary<string, List<LookupEntry>> Locations { get; init; } = new Dictionary<string, List<LookupEntry>>();
        public Dictionary<string, List<string>> OptionNames { get; init; } = new();
    }
}
