using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class PaymentMethod : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<IncomeRecord> IncomeRecords { get; set; } = new List<IncomeRecord>();
}
