using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;

namespace FMS.Infrastructure.Inventory;

/// <summary>
/// The rules a customer must satisfy, in one place because two paths check them:
/// <see cref="CustomerService.CreateCustomerAsync"/> (one customer, the endpoint a user
/// sees) and the bulk import (many customers, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// The maximum lengths are the EF configuration's (200/1000), enforced here rather than
/// left to the database: PostgreSQL rejects an over-long value with an exception, which
/// for a bulk import would be a 500 in the middle of a batch instead of a row the user
/// can fix.
/// </para>
/// </summary>
public static class CustomerRules
{
    public const int NameMaxLength = 200;
    public const int ContactInfoMaxLength = 1000;

    /// <summary>
    /// The name-conflict message, unchanged from the create endpoint. A name is the
    /// customer's identifier: it is what the unique index on (FarmId, Name) enforces, so
    /// a duplicate is an error rather than something to skip or overwrite.
    /// </summary>
    public const string DuplicateNameMessage = "A customer with this name already exists";

    /// <summary>
    /// Every field-level problem with one customer, in the order the create endpoint has
    /// always checked them, so its first-message answer is unchanged for every input it
    /// already rejected.
    /// </summary>
    public static List<FieldError> Validate(string? name, string? contactInfo)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError(CustomerImportFields.Name, "Customer name is required", "validation.customer.nameRequired"));
        else if (name.Trim().Length > NameMaxLength)
            errors.Add(new FieldError(
                CustomerImportFields.Name,
                $"Customer name cannot exceed {NameMaxLength} characters",
                "validation.customer.nameMaxLength",
                new Dictionary<string, object?> { ["max"] = NameMaxLength }));

        if (contactInfo is not null && contactInfo.Trim().Length > ContactInfoMaxLength)
            errors.Add(new FieldError(
                CustomerImportFields.ContactInfo,
                $"Contact information cannot exceed {ContactInfoMaxLength} characters",
                "validation.customer.contactInfoMaxLength",
                new Dictionary<string, object?> { ["max"] = ContactInfoMaxLength }));

        return errors;
    }

    /// <summary>
    /// Builds the customer exactly as the create endpoint does — same trimming, same
    /// defaults — so an imported row and a hand-entered one are the same record.
    /// </summary>
    public static Customer Build(Guid farmId, CreateCustomerRequest request, Guid? userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            ContactInfo = Clean(request.ContactInfo),
            CreatedBy = userId
        };

    /// <summary>Trimmed, or null when blank — the endpoint's <c>Clean</c>.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
