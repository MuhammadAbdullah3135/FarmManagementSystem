using System.Globalization;
using System.Text;
using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Application.Employees.Import;
using FMS.Application.Import;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The employee import: column mapping, the department and role resolved by name, row
/// validation against the create endpoint's own rules, the email identifier rule and the
/// all-or-nothing commit.
///
/// The relational tests compare the two paths directly — importing a row and calling
/// <see cref="IEmployeeService.CreateEmployeeAsync"/> with the same values — rather than
/// restating the expected message, because "the import says what the endpoint says" is
/// the property that has to hold, not any particular sentence.
///
/// <para>
/// Rows are built through <see cref="EmployeeRow"/>, never as hand-typed CSV. A missing
/// comma silently shifts every later column into the wrong field, which makes a test that
/// looks like it checks the department actually check the role.
/// </para>
/// </summary>
public class EmployeeImportServiceTests
{
    private const string FullHeaders =
        "firstName,lastName,email,phone,address,department,role,salaryType,salaryRate,hireDate,notes";

    private static readonly string[] FieldOrder = FullHeaders.Split(',');

    /// <summary>One full-width row: the defaults are a valid employee of this harness's farm.</summary>
    private static string EmployeeRow(params (string Field, string Value)[] values)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["firstName"] = "Amina",
            ["lastName"] = "Yusuf",
            ["email"] = "",
            ["phone"] = "",
            ["address"] = "",
            ["department"] = "Dairy",
            ["role"] = "Milker",
            ["salaryType"] = "Monthly",
            ["salaryRate"] = "45000",
            ["hireDate"] = "",
            ["notes"] = ""
        };

        foreach (var (field, value) in values)
            cells[field] = value;

        return string.Join(",", FieldOrder.Select(field => cells[field]));
    }

    private static string Csv(params string[] rows) =>
        string.Join("\n", new[] { FullHeaders }.Concat(rows)) + "\n";

    // ── the happy path ──────────────────────────────────────

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(
            EmployeeRow(("email", "amina@example.com"), ("phone", "+254 700 000000"),
                ("address", "Westlands"), ("hireDate", "2024-01-15")),
            EmployeeRow(("firstName", "Bilal"), ("lastName", "Otieno"), ("email", "bilal@example.com"),
                ("role", "Herder"), ("salaryType", "Weekly"), ("salaryRate", "8000"), ("notes", "Some notes")));

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        Assert.True(preview.IsSuccess);
        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(2, dto.ValidRowCount);
        Assert.Equal(0, dto.InvalidRowCount);
        Assert.Empty(dto.InvalidRows);
        Assert.False(dto.Truncated);

        // A preview is read-only.
        Assert.Equal(0, await harness.Context.Employees.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFilesHeaders_TheSuggestedMapping_AndTheFarmsLookups()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow())), "staff.csv", null);

        var dto = preview.Value!;
        Assert.Equal(FieldOrder, dto.Headers);
        Assert.Equal(0, dto.SuggestedMapping.For(EmployeeImportFields.FirstName)!.Column);
        Assert.Equal(FieldOrder.Length, dto.SuggestedMapping.Fields.Count);

        // The department and role pickers offer the farm's own records, and the salary
        // type offers the four words the enum defines — so a fixed value cannot invent a
        // department by typing one in.
        Assert.Equal(new[] { "Dairy" }, dto.Lookups[EmployeeImportFields.Department]);
        Assert.Equal(new[] { "Herder", "Milker" }, dto.Lookups[EmployeeImportFields.Role]);
        Assert.Equal(new[] { "Monthly", "Weekly", "Daily", "Hourly" }, dto.Lookups[EmployeeImportFields.SalaryType]);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryEmployee_WithItsDepartmentAndRoleResolvedByName()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(EmployeeRow(
            ("email", "amina@example.com"), ("phone", "+254 700 000000"), ("address", "Westlands"),
            ("hireDate", "2024-01-15"), ("notes", "First hire")));

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(1, commit.Value!.ImportedCount);
        Assert.Empty(commit.Value.InvalidRows);

        var employee = Assert.Single(await harness.Context.Employees.ToListAsync());
        Assert.Equal("Amina", employee.FirstName);
        Assert.Equal("Yusuf", employee.LastName);
        Assert.Equal("amina@example.com", employee.Email);
        Assert.Equal("+254 700 000000", employee.Phone);
        Assert.Equal("Westlands", employee.Address);
        Assert.Equal(harness.DairyId, employee.DepartmentId);
        Assert.Equal(harness.MilkerId, employee.EmployeeRoleId);
        Assert.Equal(SalaryType.Monthly, employee.SalaryType);
        Assert.Equal(45000m, employee.SalaryRate);
        Assert.Equal(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc), employee.HireDate);
        Assert.True(employee.IsActive);
        Assert.Equal("First hire", employee.Notes);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        // The same person, once by hand and once from a file. The hand-made one goes into
        // a second farm, because the same address in the import's own farm is a duplicate
        // and correctly refused — which the next tests cover.
        var byHand = await harness.Employees.CreateEmployeeAsync(Guid.NewGuid(), new CreateEmployeeRequest
        {
            FirstName = "  Amina  ",
            LastName = " Yusuf ",
            Email = " Amina@Example.com ",
            Phone = " +254 700 000000 ",
            Address = " Westlands ",
            SalaryType = SalaryType.Monthly,
            SalaryRate = 45000m,
            HireDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Notes = "First hire"
        });

        var csv = Csv(EmployeeRow(
            ("email", "Amina@Example.com"), ("phone", "+254 700 000000"), ("address", "Westlands"),
            ("hireDate", "2024-01-15"), ("notes", "First hire")));

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", null);
        Assert.Equal(1, commit.Value!.ImportedCount);

        Assert.True(byHand.IsSuccess);
        var hand = byHand.Value!;
        var imported = Assert.Single(await harness.Context.Employees
            .Where(employee => employee.FarmId == harness.FarmId)
            .ToListAsync());

        // Trimming and null-ness must not differ between the two paths.
        Assert.Equal(hand.FirstName, imported.FirstName);
        Assert.Equal(hand.LastName, imported.LastName);
        Assert.Equal(hand.Email, imported.Email);
        Assert.Equal(hand.Phone, imported.Phone);
        Assert.Equal(hand.Address, imported.Address);
        Assert.Equal(hand.SalaryType, imported.SalaryType);
        Assert.Equal(hand.SalaryRate, imported.SalaryRate);
        Assert.Equal(hand.HireDate, imported.HireDate);
        Assert.Equal(hand.Notes, imported.Notes);
    }

    [Fact]
    public async Task Commit_ConstantMapping_AppliesTheSameDepartmentAndRoleToEveryRow()
    {
        using var harness = await CreateHarnessAsync();

        // "Everyone in this file is a Dairy milker": the mapping a user reaches for when
        // their spreadsheet carries no department or role column. Those three fields are
        // required, so the mapping has to name them — which is exactly the point of the
        // fixed-value form.
        var mapping = Constants(
            (EmployeeImportFields.Department, "Dairy"),
            (EmployeeImportFields.Role, "Milker"),
            (EmployeeImportFields.SalaryType, "Monthly"),
            (EmployeeImportFields.SalaryRate, "45000"));

        var csv = "firstName,lastName\nAmina,Yusuf\nBilal,Otieno\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", mapping);

        Assert.Equal(2, commit.Value!.ImportedCount);

        var employees = await harness.Context.Employees.ToListAsync();
        Assert.All(employees, employee => Assert.Equal(harness.DairyId, employee.DepartmentId));
        Assert.All(employees, employee => Assert.Equal(harness.MilkerId, employee.EmployeeRoleId));
        Assert.All(employees, employee => Assert.Equal(SalaryType.Monthly, employee.SalaryType));
        Assert.All(employees, employee => Assert.Equal(45000m, employee.SalaryRate));
    }

    [Fact]
    public async Task Preview_ExplicitlyIgnoringARequiredField_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        // The wizard sends an empty map for a field the user cleared, which means
        // "explicitly ignored" — for a required field that is a mapping error, not a row
        // error, because no row could ever be valid.
        var mapping = new ImportMapping
        {
            Fields = new Dictionary<string, ImportFieldMap> { [EmployeeImportFields.SalaryType] = new() }
        };

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8("firstName,lastName\nAmina,Yusuf\n"), "staff.csv", mapping);

        Assert.False(preview.IsSuccess);
        Assert.Contains("Salary type", preview.Error!.Message);
        Assert.Contains("must be mapped", preview.Error.Message);
    }

    [Fact]
    public async Task Preview_DepartmentNameNotInThisFarm_IsARowErrorNamingTheValue()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("department", "Poultry")))), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.Department, error.Field);
        Assert.Equal("Department 'Poultry' was not found in this farm", error.Message);
    }

    [Fact]
    public async Task Preview_RoleFromAnotherFarm_IsARowError()
    {
        using var harness = await CreateHarnessAsync();

        // A role that exists, but for a different farm. Silently resolving it would
        // attach another farm's job title to this one's staff.
        harness.Context.EmployeeRoles.Add(new EmployeeRole
        {
            Id = Guid.NewGuid(),
            FarmId = Guid.NewGuid(),
            Name = "Head Milker"
        });
        await harness.Context.SaveChangesAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("role", "Head Milker")))), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.Role, error.Field);
        Assert.Equal("Role 'Head Milker' was not found in this farm", error.Message);
    }

    [Fact]
    public async Task Preview_DepartmentNamedTwiceInTheFarm_IsRefusedRatherThanPicked()
    {
        using var harness = await CreateHarnessAsync();

        // Two departments with the same name: choosing one for the user would attach
        // staff to the wrong section without saying so.
        harness.Context.Departments.Add(new Department
        {
            Id = Guid.NewGuid(),
            FarmId = harness.FarmId,
            Name = "Dairy"
        });
        await harness.Context.SaveChangesAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow())), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.Department, error.Field);
        Assert.Equal("Department 'Dairy' matches more than one record in this farm", error.Message);
    }

    // ── salary type and dates ───────────────────────────────

    [Theory]
    [InlineData("Monthly", SalaryType.Monthly)]
    [InlineData("weekly", SalaryType.Weekly)]
    [InlineData("DAILY", SalaryType.Daily)]
    [InlineData("Hourly", SalaryType.Hourly)]
    [InlineData("", SalaryType.Monthly)]     // blank is the enum default, as on the create request
    public async Task Commit_SalaryType_IsReadCaseInsensitively(string cell, SalaryType expected)
    {
        using var harness = await CreateHarnessAsync();

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("salaryType", cell)))), "staff.csv", null);

        Assert.Equal(1, commit.Value!.ImportedCount);
        var employee = await harness.Context.Employees.SingleAsync();
        Assert.Equal(expected, employee.SalaryType);
    }

    [Fact]
    public async Task Preview_UnknownSalaryType_IsARowErrorListingTheAllowedWords()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("salaryType", "Fortnightly")))), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.SalaryType, error.Field);
        Assert.Contains("'Fortnightly' was not recognised", error.Message);
        Assert.Contains("Monthly, Weekly, Daily, Hourly", error.Message);
    }

    [Fact]
    public async Task Preview_FutureHireDate_IsRejectedWithTheCreateEndpointsMessage()
    {
        using var harness = await CreateHarnessAsync();
        var tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("hireDate", tomorrow)))), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.HireDate, error.Field);
        Assert.Equal("Hire date cannot be in the future", error.Message);

        var endpoint = await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = "Amina",
            LastName = "Yusuf",
            HireDate = DateTime.UtcNow.AddDays(1)
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, error.Message);
    }

    [Fact]
    public async Task Preview_AmbiguousHireDate_IsReportedRatherThanGuessed()
    {
        using var harness = await CreateHarnessAsync();

        // 01/02/2023 is either 1 February or 2 January. The import refuses to pick.
        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("hireDate", "01/02/2023")))), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(EmployeeImportFields.HireDate, error.Field);
        Assert.Contains("is ambiguous", error.Message);
    }

    [Fact]
    public async Task Commit_ExplicitDateFormat_ResolvesTheAmbiguousDate()
    {
        using var harness = await CreateHarnessAsync();

        var mapping = new ImportMapping { DateFormat = "dd/MM/yyyy" };
        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("hireDate", "01/02/2023")))), "staff.csv", mapping);

        Assert.Equal(1, commit.Value!.ImportedCount);
        var employee = await harness.Context.Employees.SingleAsync();
        Assert.Equal(new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc), employee.HireDate);
    }

    // ── all-or-nothing ──────────────────────────────────────

    [Fact]
    public async Task Commit_OneInvalidRow_WritesNothingAndReportsEveryProblem()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(
            EmployeeRow(("email", "amina@example.com")),
            EmployeeRow(("firstName", "")),                                  // no first name
            EmployeeRow(("lastName", ""), ("email", "bilal@example.com")),   // no last name
            EmployeeRow(("firstName", "Cara"), ("department", "Poultry")));  // unknown department

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(4, commit.Value.TotalRows);
        Assert.Equal(3, commit.Value.InvalidRows.Count);
        Assert.Equal(0, await harness.Context.Employees.CountAsync());

        // And the refusal is visible, not silent.
        Assert.True(harness.Logs.HasMessageContaining("Refused employee import"));
        Assert.True(harness.Logs.HasMessageContaining("3 of 4 rows"));
    }

    [Fact]
    public async Task Preview_MixedFile_ReportsBothCountsWithoutWriting()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(EmployeeRow(), EmployeeRow(("firstName", "")));

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        var dto = preview.Value!;
        Assert.Equal(2, dto.TotalRows);
        Assert.Equal(1, dto.ValidRowCount);
        Assert.Equal(1, dto.InvalidRowCount);
        Assert.Equal(0, await harness.Context.Employees.CountAsync());
    }

    // ── the identifier rule, in every variant ───────────────

    [Fact]
    public async Task Preview_DuplicateEmail_ReportsExactlyWhatTheCreateEndpointReports()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingEmployeeAsync(harness, "amina@example.com");

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("email", "amina@example.com")))), "staff.csv", null);

        var row = Assert.Single(preview.Value!.InvalidRows);
        var error = Assert.Single(row.Errors);
        Assert.Equal(EmployeeImportFields.Email, error.Field);

        // A duplicate is an error, never a silent skip and never an update: the same
        // request through the create endpoint has to say the same thing.
        var endpoint = await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = "Amina",
            LastName = "Yusuf",
            Email = "amina@example.com"
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal("Conflict", endpoint.Error!.Code);
        Assert.Equal(endpoint.Error.Message, error.Message);
    }

    [Fact]
    public async Task Preview_DuplicateEmailWithinTheFile_FlagsTheLaterRow_AndNamesTheEarlierOne()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(
            EmployeeRow(("email", "amina@example.com")),
            EmployeeRow(("firstName", "Bilal"), ("email", "bilal@example.com")),
            EmployeeRow(("email", "AMINA@example.com")));

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        var row = Assert.Single(preview.Value!.InvalidRows);
        Assert.Equal(4, row.RowNumber);
        var error = Assert.Single(row.Errors);
        Assert.Equal(EmployeeImportFields.Email, error.Field);
        Assert.Contains("Duplicate email", error.Message);
        Assert.Contains("row 2", error.Message);

        // And the whole file is still refused at commit: the first row alone is not
        // enough to write, because all-or-nothing means all.
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", null);
        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(0, await harness.Context.Employees.CountAsync());
    }

    [Theory]
    [InlineData("AMINA@EXAMPLE.COM")]     // case differs
    [InlineData("  amina@example.com  ")] // padding differs
    public async Task Preview_EmailDifferingOnlyByCaseOrPadding_IsStillADuplicate(string email)
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingEmployeeAsync(harness, "amina@example.com");

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("email", email)))), "staff.csv", null);

        Assert.Equal(0, preview.Value!.ValidRowCount);
        Assert.Equal(1, preview.Value.InvalidRowCount);

        // The endpoint agrees, because both read the identifier the same way.
        var endpoint = await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = "Amina",
            LastName = "Yusuf",
            Email = email
        });

        Assert.False(endpoint.IsSuccess);
    }

    [Fact]
    public async Task Preview_DuplicateEmailAgainstAnotherFarm_IsNotADuplicate()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingEmployeeAsync(harness, "amina@example.com", farmId: Guid.NewGuid());

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("email", "amina@example.com")))), "staff.csv", null);

        Assert.Equal(1, preview.Value!.ValidRowCount);
    }

    [Fact]
    public async Task Preview_TwoEmployeesWithoutAnEmail_AreBothValid()
    {
        using var harness = await CreateHarnessAsync();

        // Without an email there is no identifier, so two such rows are two different
        // people rather than a duplicate of nothing.
        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(), EmployeeRow(("firstName", "Bilal")))), "staff.csv", null);

        Assert.Equal(2, preview.Value!.ValidRowCount);
    }

    [Fact]
    public async Task Preview_ReAddingSomeoneWhoLeft_IsAllowed()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingEmployeeAsync(harness, "amina@example.com", deleted: true);

        // Soft-deleted employees are excluded, matching how the create endpoint reads
        // employees everywhere else.
        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("email", "amina@example.com")))), "staff.csv", null);

        Assert.Equal(1, preview.Value!.ValidRowCount);
    }

    // ── the create endpoint's own messages, from the same rules ──

    [Theory]
    [InlineData("firstName", "", "First name is required")]
    [InlineData("lastName", "", "Last name is required")]
    [InlineData("salaryRate", "-1", "Salary rate cannot be negative")]
    [InlineData("email", "not-an-address", "Email address is not valid")]
    public async Task Preview_InvalidRow_SaysWhatTheCreateEndpointSays(string field, string value, string expected)
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow((field, value)))), "staff.csv", null);

        var row = Assert.Single(preview.Value!.InvalidRows);
        Assert.Contains(expected, row.Errors.Select(error => error.Message));

        // The same values through the endpoint answer with the identical sentence — the
        // rules are one function, not two copies.
        var endpoint = await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = row.Values[EmployeeImportFields.FirstName].Trim(),
            LastName = row.Values[EmployeeImportFields.LastName].Trim(),
            Email = row.Values[EmployeeImportFields.Email].Trim(),
            SalaryRate = row.Values[EmployeeImportFields.SalaryRate].Trim().Length == 0
                ? 0m
                : decimal.Parse(row.Values[EmployeeImportFields.SalaryRate], CultureInfo.InvariantCulture)
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Contains(endpoint.Error!.Message, row.Errors.Select(error => error.Message));
    }

    /// <summary>
    /// The EF column caps, which neither path checked before. PostgreSQL rejects an
    /// over-long value with an exception: in a bulk import that is a 500 in the middle of
    /// a batch instead of a row the user can fix, and on the endpoint it is a 500 instead
    /// of a validation message.
    /// </summary>
    [Theory]
    [InlineData("firstName", EmployeeImportFields.FirstName, "First name", 100)]
    [InlineData("lastName", EmployeeImportFields.LastName, "Last name", 100)]
    [InlineData("email", EmployeeImportFields.Email, "Email", 200)]
    [InlineData("phone", EmployeeImportFields.Phone, "Phone", 30)]
    [InlineData("address", EmployeeImportFields.Address, "Address", 500)]
    [InlineData("notes", EmployeeImportFields.Notes, "Notes", 1000)]
    public async Task Preview_ValueOverTheColumnCap_IsARowErrorRatherThanADatabaseException(
        string column, string fieldKey, string label, int maxLength)
    {
        using var harness = await CreateHarnessAsync();

        // The email column carries the address's own shape, so the cap is the only rule
        // it can trip beyond the address being unusable.
        var tooLong = column == "email"
            ? new string('x', maxLength) + "@example.com"
            : new string('x', maxLength + 1);
        Assert.True(tooLong.Length > maxLength);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["firstName"] = "Amina",
            ["lastName"] = "Yusuf",
            ["email"] = "amina@example.com",
            ["phone"] = "+254 700 000000",
            ["address"] = "Westlands",
            ["notes"] = "Notes"
        };
        values[column] = tooLong;

        var csv = Csv(EmployeeRow(values.Select(pair => (pair.Key, pair.Value)).ToArray()));

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        var error = Assert.Single(Assert.Single(preview.Value!.InvalidRows).Errors);
        Assert.Equal(fieldKey, error.Field);
        Assert.Equal($"{label} cannot exceed {maxLength} characters", error.Message);

        // And the endpoint refuses the same value with the same words.
        var endpoint = await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = values["firstName"],
            LastName = values["lastName"],
            Email = values["email"],
            Phone = values["phone"],
            Address = values["address"],
            Notes = values["notes"]
        });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(error.Message, endpoint.Error!.Message);
    }

    // ── the commit revalidates from the file ────────────────

    [Fact]
    public async Task Commit_RevalidatesFromTheFile_SoAnEmployeeCreatedAfterThePreviewBlocksIt()
    {
        using var harness = await CreateHarnessAsync();

        var csv = Csv(EmployeeRow(("email", "amina@example.com")));
        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "staff.csv", null);
        Assert.Equal(1, preview.Value!.ValidRowCount);

        // Someone adds the same employee between the preview and the commit.
        await harness.Employees.CreateEmployeeAsync(harness.FarmId, new CreateEmployeeRequest
        {
            FirstName = "Amina",
            LastName = "Yusuf",
            Email = "amina@example.com"
        });

        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "staff.csv", null);

        Assert.True(commit.IsSuccess);
        Assert.Equal(0, commit.Value!.ImportedCount);
        var row = Assert.Single(commit.Value.InvalidRows);
        Assert.Equal("An employee with this email already exists", Assert.Single(row.Errors).Message);

        // Still exactly the one employee that was created by hand.
        Assert.Equal(1, await harness.Context.Employees.CountAsync());
    }

    // ── limits ──────────────────────────────────────────────

    [Fact]
    public async Task Preview_MoreRowsThanTheConfiguredLimit_IsRefused()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxRows = 2);

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(), EmployeeRow(), EmployeeRow())), "staff.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("more than 2", preview.Error!.Message);
    }

    [Fact]
    public async Task Preview_CapsTheReportedProblemRows()
    {
        using var harness = await CreateHarnessAsync(options => options.MaxReportedRows = 1);

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8(Csv(EmployeeRow(("firstName", "")), EmployeeRow(("firstName", "")))), "staff.csv", null);

        var dto = preview.Value!;
        Assert.Equal(2, dto.InvalidRowCount);
        Assert.Single(dto.InvalidRows);
        // Says so rather than truncating silently: the counts still cover the whole file.
        Assert.True(dto.Truncated);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n"), "staff.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("no data rows", preview.Error!.Message);
    }

    // ── harness ─────────────────────────────────────────────

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Guid FarmId { get; init; }
        public required Guid DairyId { get; init; }
        public required Guid MilkerId { get; init; }
        public required EmployeeImportService Service { get; init; }
        public required IEmployeeService Employees { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }

        public void Dispose() => Context.Dispose();
    }

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static ImportMapping Constants(params (string Field, string Value)[] values) =>
        new()
        {
            Fields = values.ToDictionary(
                pair => pair.Field,
                pair => new ImportFieldMap { Constant = pair.Value })
        };

    private static async Task<Harness> CreateHarnessAsync(Action<ImportOptions>? configure = null)
    {
        var context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Import Farm" };
        var dairy = new Department { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Dairy" };
        var milker = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Milker" };
        var herder = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Herder" };

        context.Farms.Add(farm);
        context.Departments.Add(dairy);
        context.EmployeeRoles.AddRange(milker, herder);
        await context.SaveChangesAsync();

        var (logger, logs) = TestLoggers.Create<EmployeeImportService>();
        var employees = new EmployeeService(context, new FixedCurrentUser());

        var options = new ImportOptions();
        configure?.Invoke(options);

        return new Harness
        {
            Context = context,
            FarmId = farm.Id,
            DairyId = dairy.Id,
            MilkerId = milker.Id,
            Service = new EmployeeImportService(
                new SpreadsheetReader(), employees, context, Options.Create(options), logger),
            Employees = employees,
            Logs = logs
        };
    }

    private static async Task SeedExistingEmployeeAsync(
        Harness harness, string email, Guid? farmId = null, bool deleted = false)
    {
        harness.Context.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farmId ?? harness.FarmId,
            FirstName = "Existing",
            LastName = "Person",
            Email = email,
            IsDeleted = deleted
        });

        await harness.Context.SaveChangesAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
