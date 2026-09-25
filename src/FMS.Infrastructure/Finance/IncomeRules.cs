using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Application.Finance.Import;
using FMS.Domain.Entities;

namespace FMS.Infrastructure.Finance;

/// <summary>
/// The field rules an income record must satisfy, in one place because two paths check
/// them: <see cref="FinanceService.CreateIncomeRecordAsync"/> (one record, the endpoint
/// a user sees) and the bulk import (many records, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// As with an expense, there is deliberately <b>no duplicate rule</b>: an income record
/// has no identifier, a unique index would reject legitimate repeated sales, and the
/// create endpoint enforces no duplicate. Inventing one here would make an imported file
/// stricter than the add form.
/// </para>
/// </summary>
public static class IncomeRules
{
    public const int DescriptionMaxLength = 1000;

    /// <summary>
    /// Every field-level problem with one record, in the order the create endpoint has
    /// always checked them (amount, date, description), so its first-message answer is
    /// unchanged for every input it already rejected.
    /// </summary>
    public static List<FieldError> Validate(decimal amount, DateTime? incomeDate, string? description)
    {
        var errors = new List<FieldError>();

        if (amount <= 0)
            errors.Add(new FieldError(IncomeImportFields.Amount, "Amount must be greater than zero", "validation.income.amountPositive"));

        if (incomeDate.HasValue && incomeDate.Value > DateTime.UtcNow.AddMinutes(5))
            errors.Add(new FieldError(IncomeImportFields.IncomeDate, "Income date cannot be in the future", "validation.income.dateNotFuture"));

        if (description is not null && description.Length > DescriptionMaxLength)
            errors.Add(new FieldError(
                IncomeImportFields.Description,
                $"Description cannot exceed {DescriptionMaxLength} characters",
                "validation.income.descriptionMaxLength",
                new Dictionary<string, object?> { ["max"] = DescriptionMaxLength }));

        return errors;
    }

    /// <summary>
    /// Builds the record exactly as the create endpoint does — same rounding, same
    /// blank-date default, same trimming — so an imported row and a hand-entered one are
    /// the same record.
    /// </summary>
    public static IncomeRecord Build(Guid farmId, CreateIncomeRecordRequest request, Guid? userId, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            IncomeDate = request.IncomeDate ?? now,
            Amount = Math.Round(request.Amount, 2),
            IncomeCategoryId = request.IncomeCategoryId,
            PaymentMethodId = request.PaymentMethodId,
            AnimalId = request.AnimalId,
            LocationId = request.LocationId,
            Description = request.Description?.Trim(),
            CreatedAt = now,
            CreatedBy = userId
        };
}
