using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Application.Employees.Import;
using FMS.Domain.Entities;
using FMS.Domain.Enums;

namespace FMS.Infrastructure.Employees;

/// <summary>
/// The rules an employee must satisfy, in one place because two paths check them:
/// <see cref="EmployeeService.CreateEmployeeAsync"/> (one employee, the endpoint a user
/// sees) and the bulk import (many employees, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// Two things were added here that neither path checked before:
/// </para>
/// <list type="number">
/// <item>
/// <b>The column caps.</b> The EF configuration allows a 30-character phone, a
/// 200-character email, a 500-character address and a 1000-character note, and nothing
/// validated any of them. PostgreSQL rejects an over-long value with an exception, so
/// the create endpoint answered 500 and a bulk import would have failed mid-batch.
/// </item>
/// <item>
/// <b>Email uniqueness.</b> An employee has no code column, so the email is the
/// identifier — and it is enforced in both paths, because an import that may create what
/// the endpoint refuses is not parity, it is a bypass. Soft-deleted employees are
/// excluded, matching how the create endpoint reads employees everywhere else, so
/// re-adding someone who left is allowed.
/// </item>
/// </list>
/// </summary>
public static class EmployeeRules
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 30;
    public const int EmailMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int NotesMaxLength = 1000;

    /// <summary>The identifier-conflict message, in the shape the inventory rule uses.</summary>
    public const string DuplicateEmailMessage = "An employee with this email already exists";

    /// <summary>
    /// Every field-level problem with one employee, in the order the create endpoint has
    /// always checked them, so its first-message answer is unchanged for every input it
    /// already rejected.
    /// </summary>
    public static List<FieldError> ValidateDetails(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        string? address,
        decimal salaryRate,
        DateTime? hireDate,
        string? notes)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(firstName))
            errors.Add(new FieldError(EmployeeImportFields.FirstName, "First name is required"));
        else if (firstName.Length > NameMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.FirstName, $"First name cannot exceed {NameMaxLength} characters"));

        if (string.IsNullOrWhiteSpace(lastName))
            errors.Add(new FieldError(EmployeeImportFields.LastName, "Last name is required"));
        else if (lastName.Length > NameMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.LastName, $"Last name cannot exceed {NameMaxLength} characters"));

        if (!string.IsNullOrWhiteSpace(email))
        {
            var trimmed = email.Trim();

            // The endpoint's own loose check, unchanged: a full RFC parse rejects
            // addresses that providers accept, and the provider is the authority.
            if (!trimmed.Contains('@') || trimmed.StartsWith("@") || trimmed.EndsWith("@"))
                errors.Add(new FieldError(EmployeeImportFields.Email, "Email address is not valid"));
        }

        if (salaryRate < 0)
            errors.Add(new FieldError(EmployeeImportFields.SalaryRate, "Salary rate cannot be negative"));

        if (hireDate.HasValue && hireDate.Value.Date > DateTime.UtcNow.Date)
            errors.Add(new FieldError(EmployeeImportFields.HireDate, "Hire date cannot be in the future"));

        // The column caps come last on purpose. They are new, so a request that used to
        // be rejected keeps answering with exactly the message it answered with before —
        // the cap only decides the message for input nothing used to reject at all.
        if (!string.IsNullOrWhiteSpace(email) && email.Trim().Length > EmailMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.Email, $"Email cannot exceed {EmailMaxLength} characters"));

        if (phone is not null && phone.Trim().Length > PhoneMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.Phone, $"Phone cannot exceed {PhoneMaxLength} characters"));

        if (address is not null && address.Trim().Length > AddressMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.Address, $"Address cannot exceed {AddressMaxLength} characters"));

        if (notes is not null && notes.Trim().Length > NotesMaxLength)
            errors.Add(new FieldError(EmployeeImportFields.Notes, $"Notes cannot exceed {NotesMaxLength} characters"));

        return errors;
    }

    /// <summary>
    /// The comparison key for the identifier: trimmed and lower-cased, because an email
    /// address is not case-sensitive in practice and "A@x.com" versus "a@x.com" is the
    /// same person. Null when there is no email — an employee without one is not
    /// identified by anything, so it can never be a duplicate.
    /// </summary>
    public static string? EmailKey(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Builds the employee exactly as the create endpoint does — same trimming, same
    /// rounding, same defaults — so an imported row and a hand-entered one are the same
    /// record.
    /// </summary>
    public static Employee Build(Guid farmId, CreateEmployeeRequest request, Guid? userId, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            DepartmentId = request.DepartmentId,
            EmployeeRoleId = request.EmployeeRoleId,
            SalaryType = request.SalaryType,
            SalaryRate = Math.Round(request.SalaryRate, 2),
            HireDate = request.HireDate,
            IsActive = true,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedBy = userId
        };
}
