using FMS.Domain.Enums;

namespace FMS.Application.Tasks;

public class FarmTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FarmTaskPriority Priority { get; set; }
    public string PriorityName { get; set; } = string.Empty;
    public FarmTaskStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public bool IsOverdue { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletionNotes { get; set; }
    public string? CancelReason { get; set; }

    public Guid? AssignedEmployeeId { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public Guid? AnimalId { get; set; }
    public string? AnimalName { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class CreateFarmTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FarmTaskPriority Priority { get; set; } = FarmTaskPriority.Medium;
    public DateTime DueDate { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
}

public class UpdateFarmTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FarmTaskPriority Priority { get; set; } = FarmTaskPriority.Medium;
    public DateTime DueDate { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
}

public class CompleteFarmTaskRequest
{
    public string? CompletionNotes { get; set; }
}

public class CancelFarmTaskRequest
{
    public string? Reason { get; set; }
}

public class FarmTaskListFilter
{
    public string? Search { get; set; }
    public FarmTaskStatus? Status { get; set; }
    public FarmTaskPriority? Priority { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public bool IncludeOverdueOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
