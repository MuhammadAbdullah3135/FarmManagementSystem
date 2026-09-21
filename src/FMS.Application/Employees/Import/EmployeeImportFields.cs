using FMS.Application.Import;

namespace FMS.Application.Employees.Import;

/// <summary>
/// The employee importer's field vocabulary.
///
/// Three of these resolve against the farm rather than being free text — the
/// department, the employee role and the salary type — because those are the fields a
/// spreadsheet cannot get right on its own: a name has to find the farm's own record,
/// and a salary type has to be one of four words.
/// </summary>
public static class EmployeeImportFields
{
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Address = "address";
    public const string Department = "department";
    public const string Role = "role";
    public const string SalaryType = "salaryType";
    public const string SalaryRate = "salaryRate";
    public const string HireDate = "hireDate";
    public const string Notes = "notes";

    /// <summary>Fields whose values must match something the farm already has.</summary>
    public static readonly IReadOnlyList<string> LookupFields = new[]
    {
        Department, Role, SalaryType
    };

    /// <summary>Fields carrying a date, parsed with the ambiguous-format guard.</summary>
    public static readonly IReadOnlyList<string> DateFields = new[] { HireDate };

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(FirstName, "First name", true,
            "e.g. Amina"),
        new ImportFieldDescriptor(LastName, "Last name", true,
            "e.g. Yusuf"),
        new ImportFieldDescriptor(Email, "Email", false,
            "This is the employee's identifier: a duplicate is refused"),
        new ImportFieldDescriptor(Phone, "Phone", false,
            "e.g. +254 700 000000"),
        new ImportFieldDescriptor(Address, "Address", false,
            "Free text"),
        new ImportFieldDescriptor(Department, "Department", true,
            "Must match a department in this farm"),
        new ImportFieldDescriptor(Role, "Role", true,
            "Must match an employee role in this farm"),
        new ImportFieldDescriptor(SalaryType, "Salary type", true,
            "Monthly, Weekly, Daily or Hourly"),
        new ImportFieldDescriptor(SalaryRate, "Salary rate", true,
            "Amount per salary type, e.g. 45000"),
        new ImportFieldDescriptor(HireDate, "Hire date", false,
            "yyyy-MM-dd, or a real Excel date cell"),
        new ImportFieldDescriptor(Notes, "Notes", false,
            "Free text"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no mapping.
    /// Matching is done on the normalized header, so spacing, case and punctuation are
    /// irrelevant.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [FirstName] = new[] { "first name", "firstname", "given name", "first" },
        [LastName] = new[] { "last name", "lastname", "surname", "family name", "last" },
        [Email] = new[] { "email", "email address", "e mail", "mail" },
        [Phone] = new[] { "phone", "phone number", "mobile", "telephone", "contact", "contact number" },
        [Address] = new[] { "address", "home address", "residence", "location" },
        [Department] = new[] { "department", "dept", "section", "unit" },
        [Role] = new[] { "role", "job role", "position", "job title", "title", "designation" },
        [SalaryType] = new[] { "salary type", "pay type", "pay period", "salarytype", "frequency" },
        [SalaryRate] = new[] { "salary rate", "salary", "rate", "wage", "pay", "amount", "basic pay" },
        [HireDate] = new[] { "hire date", "hired", "start date", "date hired", "joined", "employment date", "hiredate" },
        [Notes] = new[] { "notes", "note", "comments", "remarks" },
    };

    /// <summary>
    /// Declared last on purpose: static fields initialize in textual order, and this one
    /// is built from <see cref="All"/> and <see cref="Aliases"/> above.
    /// </summary>
    private static readonly ImportFieldCatalog Catalog = new(All, Aliases);

    /// <summary>This vocabulary as the shared pipeline consumes it.</summary>
    public static IImportFieldCatalog FieldCatalog => Catalog;

    /// <summary>
    /// The salary types a row may name, in a stable order. Read from the enum rather
    /// than repeated as strings, so a new salary type cannot be forgotten here.
    /// </summary>
    public static IReadOnlyList<string> SalaryTypeNames { get; } =
        Enum.GetNames<Domain.Enums.SalaryType>();

    public static ImportFieldDescriptor? Describe(string key) => Catalog.Describe(key);

    public static string Normalize(string? value) => ImportFieldCatalog.Normalize(value);

    public static string? MatchHeader(string header) => Catalog.MatchHeader(header);
}
