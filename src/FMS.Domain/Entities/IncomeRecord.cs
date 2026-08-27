using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class IncomeRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public DateTime IncomeDate { get; set; }
    public decimal Amount { get; set; }

    public Guid IncomeCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }

    /// <summary>Optional animal link for traceability (e.g. sale proceeds from one animal).</summary>
    public Guid? AnimalId { get; set; }

    /// <summary>Optional location link for traceability.</summary>
    public Guid? LocationId { get; set; }

    public string? Description { get; set; }

    public Farm Farm { get; set; } = null!;
    public IncomeCategory Category { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public Animal? Animal { get; set; }
    public Location? Location { get; set; }
}
