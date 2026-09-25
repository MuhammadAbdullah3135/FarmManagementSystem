using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;

namespace FMS.Infrastructure.Inventory;

/// <summary>
/// The rules a supplier must satisfy, in one place because two paths check them:
/// <see cref="SupplierService.CreateSupplierAsync"/> (one supplier, the endpoint a user
/// sees) and the bulk import (many suppliers, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// The maximum lengths are the EF configuration's (200/1000/2000), enforced here rather
/// than left to the database: PostgreSQL rejects an over-long value with an exception,
/// which for a bulk import would be a 500 in the middle of a batch instead of a row the
/// user can fix.
/// </para>
/// </summary>
public static class SupplierRules
{
    public const int NameMaxLength = 200;
    public const int ContactInfoMaxLength = 1000;
    public const int ProductsSuppliedMaxLength = 2000;

    /// <summary>
    /// The name-conflict message, unchanged from the create endpoint. A name is the
    /// supplier's identifier: it is what the unique index on (FarmId, Name) enforces, so
    /// a duplicate is an error rather than something to skip or overwrite.
    /// </summary>
    public const string DuplicateNameMessage = "A supplier with this name already exists";

    /// <summary>
    /// Every field-level problem with one supplier, in the order the create endpoint has
    /// always checked them, so its first-message answer is unchanged for every input it
    /// already rejected.
    /// </summary>
    public static List<FieldError> Validate(string? name, string? contactInfo, string? productsSupplied)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError(SupplierImportFields.Name, "Supplier name is required", "validation.supplier.nameRequired"));
        else if (name.Trim().Length > NameMaxLength)
            errors.Add(new FieldError(
                SupplierImportFields.Name,
                $"Supplier name cannot exceed {NameMaxLength} characters",
                "validation.supplier.nameMaxLength",
                new Dictionary<string, object?> { ["max"] = NameMaxLength }));

        if (contactInfo is not null && contactInfo.Trim().Length > ContactInfoMaxLength)
            errors.Add(new FieldError(
                SupplierImportFields.ContactInfo,
                $"Contact information cannot exceed {ContactInfoMaxLength} characters",
                "validation.supplier.contactInfoMaxLength",
                new Dictionary<string, object?> { ["max"] = ContactInfoMaxLength }));

        if (productsSupplied is not null && productsSupplied.Trim().Length > ProductsSuppliedMaxLength)
            errors.Add(new FieldError(
                SupplierImportFields.ProductsSupplied,
                $"Products supplied cannot exceed {ProductsSuppliedMaxLength} characters",
                "validation.supplier.productsSuppliedMaxLength",
                new Dictionary<string, object?> { ["max"] = ProductsSuppliedMaxLength }));

        return errors;
    }

    /// <summary>
    /// Builds the supplier exactly as the create endpoint does — same trimming, same
    /// defaults — so an imported row and a hand-entered one are the same record.
    /// </summary>
    public static Supplier Build(Guid farmId, CreateSupplierRequest request, Guid? userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            ContactInfo = Clean(request.ContactInfo),
            ProductsSupplied = Clean(request.ProductsSupplied),
            CreatedBy = userId
        };

    /// <summary>Trimmed, or null when blank — the endpoint's <c>Clean</c>.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
