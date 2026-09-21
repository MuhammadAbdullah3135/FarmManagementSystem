using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Application.Employees.Import;
using FMS.Application.Import;
using FMS.Domain.Enums;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Import;

/// <summary>
/// The bulk employee import: read the file, map its columns, resolve the farm's
/// departments and roles, validate every row against the same rules the create endpoint
/// uses, then commit the batch atomically.
///
/// The mechanics — reading, header detection, the mapping merge, value reading, date
/// and number parsing, per-row reporting and both reports — live in
/// <see cref="ImportPipeline"/>. What is below is only what an employee is.
///
/// <para>
/// The relational rules are the department and the role (both resolved by name against
/// the farm) and the email, which is the employee's identifier: a duplicate — against an
/// existing employee or against another row of the same file — is a row error, never a
/// silent skip and never an update. The check against existing employees is delegated to
/// <see cref="IEmployeeService"/>, which owns it, so the two paths cannot disagree.
/// </para>
/// </summary>
public class EmployeeImportService : IEmployeeImportService
{
    private readonly ISpreadsheetReader _reader;
    private readonly IEmployeeService _employees;
    private readonly FmsDbContext _context;
    private readonly ImportOptions _options;
    private readonly ILogger<EmployeeImportService> _logger;

    public EmployeeImportService(
        ISpreadsheetReader reader,
        IEmployeeService employees,
        FmsDbContext context,
        IOptions<ImportOptions> options,
        ILogger<EmployeeImportService> logger)
    {
        _reader = reader;
        _employees = employees;
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
            ImportPipeline.BuildPreview(analysis, EmployeeImportFields.All, _options));
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
                "Refused employee import into farm {FarmId} from {FileName}: {InvalidRowCount} of {TotalRows} rows failed validation",
                farmId, fileName, analysis.Rows.Count - locallyValid.Count, analysis.Rows.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        // The commit revalidates from the file's own rows rather than trusting the
        // preview, so an employee added between the two calls is caught here and still
        // writes nothing.
        var creation = await _employees.CreateEmployeesAsync(
            farmId, locallyValid.Select(row => row.Request!).ToList());

        if (!creation.IsSuccess)
            return Result<ImportCommitDto>.Failure(creation.Error!);

        if (creation.Value!.Failures.Count > 0)
        {
            ApplyFailures(locallyValid, creation.Value.Failures);
            _logger.LogWarning(
                "Refused employee import into farm {FarmId} from {FileName}: {FailureCount} rows failed validation at commit time",
                farmId, fileName, creation.Value.Failures.Count);

            return Result<ImportCommitDto>.Success(
                ImportPipeline.BuildCommit(analysis, _options, importedCount: 0));
        }

        _logger.LogInformation(
            "Imported {ImportedCount} employees into farm {FarmId} from {FileName}",
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

        var suggested = ImportPipeline.SuggestMapping(sheet.Headers, EmployeeImportFields.FieldCatalog);
        var effective = ImportPipeline.Merge(mapping, suggested, EmployeeImportFields.All);

        var mappingError = ImportPipeline.ValidateMapping(effective, sheet.Headers.Count, EmployeeImportFields.All);
        if (mappingError != null)
            return Result<Analysis>.Failure(mappingError);

        var lookupResult = await LoadLookupsAsync(farmId);
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

        // Which email first appears on which row, so a repeated address inside the file
        // can be reported against the later row and name the earlier one.
        var firstRowByEmail = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var key = EmployeeRules.EmailKey(
                ImportPipeline.ReadValue(sheet.Rows[index], effective, EmployeeImportFields.Email));
            if (key is not null && !firstRowByEmail.ContainsKey(key))
                firstRowByEmail[key] = index;
        }

        for (var index = 0; index < sheet.Rows.Count; index++)
            analysis.Rows.Add(BuildRow(sheet, index, firstRowByEmail, lookups, effective));

        return Result<Analysis>.Success(analysis);
    }

    private static RowAnalysis BuildRow(
        SpreadsheetSheet sheet,
        int index,
        Dictionary<string, int> firstRowByEmail,
        Lookups lookups,
        ImportMapping mapping)
    {
        var spreadsheetRow = sheet.Rows[index];
        var row = new RowAnalysis { RowNumber = spreadsheetRow.RowNumber };
        var errors = row.Errors;

        ImportPipeline.ReadValues(spreadsheetRow, mapping, EmployeeImportFields.All, row.Values);

        var values = row.Values;
        var request = new CreateEmployeeRequest
        {
            FirstName = values[EmployeeImportFields.FirstName].Trim(),
            LastName = values[EmployeeImportFields.LastName].Trim(),
            Email = ImportPipeline.Trimmed(values[EmployeeImportFields.Email]),
            Phone = ImportPipeline.Trimmed(values[EmployeeImportFields.Phone]),
            Address = ImportPipeline.Trimmed(values[EmployeeImportFields.Address]),
            Notes = ImportPipeline.Trimmed(values[EmployeeImportFields.Notes])
        };

        // An address repeated inside the file is a file problem, so it is reported here
        // with both row numbers rather than left to the batch, which only knows positions.
        if (EmployeeRules.EmailKey(request.Email) is { } emailKey &&
            firstRowByEmail.TryGetValue(emailKey, out var firstIndex) &&
            firstIndex != index)
        {
            errors.Add(ImportPipeline.FieldError(EmployeeImportFields.Email,
                $"Duplicate email '{request.Email}' in this file: row {sheet.Rows[firstIndex].RowNumber} already uses it and is the one that will be imported."));
        }

        request.DepartmentId = ImportPipeline.Resolve(
            lookups.Departments, values[EmployeeImportFields.Department],
            EmployeeImportFields.Department, "Department", errors)?.Id;
        request.EmployeeRoleId = ImportPipeline.Resolve(
            lookups.Roles, values[EmployeeImportFields.Role],
            EmployeeImportFields.Role, "Role", errors)?.Id;

        request.SalaryType = ResolveSalaryType(values[EmployeeImportFields.SalaryType], errors);
        request.SalaryRate = ImportPipeline.ParseDecimalField(
            values[EmployeeImportFields.SalaryRate], EmployeeImportFields.SalaryRate, "Salary rate", errors);
        request.HireDate = ImportPipeline.ParseDateField(
            values[EmployeeImportFields.HireDate], EmployeeImportFields.HireDate, "Hire date",
            mapping.DateFormat, errors);

        // The create endpoint's own rules, so a row cannot be accepted here where a
        // hand-entered employee would be rejected — or the other way round.
        foreach (var error in EmployeeRules.ValidateDetails(
            request.FirstName, request.LastName, request.Email, request.Phone,
            request.Address, request.SalaryRate, request.HireDate, request.Notes))
        {
            if (errors.Any(existing => existing.Field == error.Field))
                continue;

            errors.Add(ImportPipeline.FieldError(error.Field, error.Message));
        }

        row.Request = request;
        return row;
    }

    /// <summary>
    /// The salary type is one of four words, read from the enum rather than repeated as
    /// strings. Left blank it is the enum's default, which is what a hand-entered
    /// employee with no salary type set gets — the create request's own default.
    /// </summary>
    private static SalaryType ResolveSalaryType(string raw, List<ImportRowErrorDto> errors)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return default;

        if (Enum.TryParse<SalaryType>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors.Add(ImportPipeline.FieldError(EmployeeImportFields.SalaryType,
            $"Salary type '{value}' was not recognised. Use {string.Join(", ", EmployeeImportFields.SalaryTypeNames)}."));

        return default;
    }

    private async Task ApplyFarmValidationAsync(Guid farmId, List<RowAnalysis> rows)
    {
        var result = await _employees.ValidateEmployeesAsync(farmId, rows.Select(row => row.Request!).ToList());

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
    /// The farm's departments and employee roles by name, plus the names for the wizard's
    /// fixed-value picker. Note these are the farm's *employee* roles (a job title such
    /// as "Milker"), not the account roles that drive authorization.
    /// </summary>
    private async Task<Result<Lookups>> LoadLookupsAsync(Guid farmId)
    {
        var departments = await _context.Departments
            .Where(department => department.FarmId == farmId)
            .Select(department => new { department.Id, department.Name })
            .ToListAsync();

        var roles = await _context.EmployeeRoles
            .Where(role => role.FarmId == farmId)
            .Select(role => new { role.Id, role.Name })
            .ToListAsync();

        return Result<Lookups>.Success(new Lookups
        {
            Departments = ImportPipeline.GroupByName(
                departments.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            Roles = ImportPipeline.GroupByName(
                roles.Select(item => new ImportPipeline.LookupEntry(item.Id, item.Name, null))),
            OptionNames = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                [EmployeeImportFields.Department] = ImportPipeline.Names(departments.Select(item => item.Name)),
                [EmployeeImportFields.Role] = ImportPipeline.Names(roles.Select(item => item.Name)),
                [EmployeeImportFields.SalaryType] = EmployeeImportFields.SalaryTypeNames.ToList()
            }
        });
    }

    /// <summary>
    /// The batch reports a message, not a field, so the field is inferred here. This is
    /// presentation only — the message itself is unchanged, which is what keeps a row's
    /// error text identical to what the create endpoint answers.
    /// </summary>
    private static readonly (string Prefix, string Field)[] MessageFieldPrefixes =
    {
        ("An employee with this email", EmployeeImportFields.Email),
        ("The batch contains the same email", EmployeeImportFields.Email),
        ("First name is required", EmployeeImportFields.FirstName),
        ("First name cannot exceed", EmployeeImportFields.FirstName),
        ("Last name is required", EmployeeImportFields.LastName),
        ("Last name cannot exceed", EmployeeImportFields.LastName),
        ("Email address is not valid", EmployeeImportFields.Email),
        ("Email cannot exceed", EmployeeImportFields.Email),
        ("Phone cannot exceed", EmployeeImportFields.Phone),
        ("Address cannot exceed", EmployeeImportFields.Address),
        ("Notes cannot exceed", EmployeeImportFields.Notes),
        ("Salary rate cannot be negative", EmployeeImportFields.SalaryRate),
        ("Hire date cannot be in the future", EmployeeImportFields.HireDate),
        ("Department not found", EmployeeImportFields.Department),
        ("Role not found", EmployeeImportFields.Role),
    };

    private sealed class Analysis : ImportAnalysis<RowAnalysis>;

    private sealed class RowAnalysis : ImportRowAnalysis
    {
        public CreateEmployeeRequest? Request { get; set; }
    }

    private sealed class Lookups
    {
        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Departments { get; init; } =
            new Dictionary<string, List<ImportPipeline.LookupEntry>>();

        public IReadOnlyDictionary<string, List<ImportPipeline.LookupEntry>> Roles { get; init; } =
            new Dictionary<string, List<ImportPipeline.LookupEntry>>();

        public Dictionary<string, List<string>> OptionNames { get; init; } = new();
    }
}
