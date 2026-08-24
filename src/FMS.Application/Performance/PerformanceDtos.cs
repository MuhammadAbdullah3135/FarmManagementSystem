namespace FMS.Application.Performance;

public class PerformanceReviewDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime ReviewDate { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? Strengths { get; set; }
    public string? AreasForImprovement { get; set; }
    public string? Comments { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePerformanceReviewRequest
{
    public Guid EmployeeId { get; set; }
    public int Rating { get; set; }
    public DateTime ReviewDate { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? Strengths { get; set; }
    public string? AreasForImprovement { get; set; }
    public string? Comments { get; set; }
}

public class UpdatePerformanceReviewRequest
{
    public int Rating { get; set; }
    public DateTime ReviewDate { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? Strengths { get; set; }
    public string? AreasForImprovement { get; set; }
    public string? Comments { get; set; }
}

public class PerformanceReviewListFilter
{
    public Guid? EmployeeId { get; set; }
    public int? MinRating { get; set; }
    public int? MaxRating { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
