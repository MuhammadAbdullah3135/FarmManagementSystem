using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Application.Finance.Import;
using FMS.Domain.Entities;

namespace FMS.Infrastructure.Finance;

/// <summary>
/// The field rules an expense must satisfy, in one place because two paths check them:
/// <see cref="FinanceService.CreateExpenseAsync"/> (one expense, the endpoint a user
/// sees) and the bulk import (many expenses, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// There is deliberately <b>no duplicate rule</b>. An expense has no identifier: no code
/// column, and a unique index would reject legitimate repeated transactions (two feed
/// purchases of the same amount on the same day). The create endpoint enforces no
/// duplicate, so the importer must not invent one — a rule the endpoint does not have
/// would make the file stricter than the add form. The importer's duplicate policy for
/// expenses is therefore "none", disclosed in the API docs and the wizard.
/// </para>
/// </summary>
public static class ExpenseRules
{
    public const int DescriptionMaxLength = 1000;

    /// <summary>
    /// Every field-level problem with one expense, in the order the create endpoint has
    /// always checked them (amount, date, description), so its first-message answer is
    /// unchanged for every input it already rejected.
    /// </summary>
    public static List<FieldError> Validate(decimal amount, DateTime? expenseDate, string? description)
    {
        var errors = new List<FieldError>();

        if (amount <= 0)
            errors.Add(new FieldError(ExpenseImportFields.Amount, "Amount must be greater than zero", "validation.expense.amountPositive"));

        // A blank date means "now" at the endpoint, which can never be in the future, so
        // only an explicit date is checked.
        if (expenseDate.HasValue && expenseDate.Value > DateTime.UtcNow.AddMinutes(5))
            errors.Add(new FieldError(ExpenseImportFields.ExpenseDate, "Expense date cannot be in the future", "validation.expense.dateNotFuture"));

        // Raw length, not trimmed: the endpoint checks the request's own string.
        if (description is not null && description.Length > DescriptionMaxLength)
            errors.Add(new FieldError(
                ExpenseImportFields.Description,
                $"Description cannot exceed {DescriptionMaxLength} characters",
                "validation.expense.descriptionMaxLength",
                new Dictionary<string, object?> { ["max"] = DescriptionMaxLength }));

        return errors;
    }

    /// <summary>
    /// Builds the expense exactly as the create endpoint does — same rounding, same
    /// blank-date default, same trimming — so an imported row and a hand-entered one are
    /// the same record.
    /// </summary>
    public static Expense Build(Guid farmId, CreateExpenseRequest request, Guid? userId, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ExpenseDate = request.ExpenseDate ?? now,
            Amount = Math.Round(request.Amount, 2),
            ExpenseCategoryId = request.ExpenseCategoryId,
            PaymentMethodId = request.PaymentMethodId,
            AnimalId = request.AnimalId,
            LocationId = request.LocationId,
            Description = request.Description?.Trim(),
            CreatedAt = now,
            CreatedBy = userId
        };
}
