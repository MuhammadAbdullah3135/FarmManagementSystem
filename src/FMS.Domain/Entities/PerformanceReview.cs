using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>A periodic performance evaluation of an employee.</summary>
public class PerformanceReview : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Overall rating from 1 (poor) to 5 (excellent).</summary>
    public int Rating { get; set; }
    public DateTime ReviewDate { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }

    public string? Strengths { get; set; }
    public string? AreasForImprovement { get; set; }
    public string? Comments { get; set; }

    /// <summary>User id of the reviewer.</summary>
    public Guid? ReviewedBy { get; set; }

    public Employee Employee { get; set; } = null!;
}
