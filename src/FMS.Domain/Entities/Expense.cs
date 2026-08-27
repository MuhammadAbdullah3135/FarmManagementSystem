using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Expense : AuditableEntity
{
    public Guid FarmId { get; set; }
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }

    public Guid ExpenseCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }

    /// <summary>Optional animal link for traceability (e.g. veterinary cost for one animal).</summary>
    public Guid? AnimalId { get; set; }

    /// <summary>Optional location link for traceability.</summary>
    public Guid? LocationId { get; set; }

    public string? Description { get; set; }

    public Farm Farm { get; set; } = null!;
    public ExpenseCategory Category { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public Animal? Animal { get; set; }
    public Location? Location { get; set; }
}
