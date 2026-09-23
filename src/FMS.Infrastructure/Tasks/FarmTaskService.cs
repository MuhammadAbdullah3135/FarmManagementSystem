using FMS.Application.Common;
using FMS.Application.Tasks;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Common;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Tasks;

public class FarmTaskService : IFarmTaskService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FarmTaskService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // Queries

    public async Task<Result<FarmTaskDto>> GetTaskByIdAsync(Guid farmId, Guid id)
    {
        var task = await QueryTasks(farmId)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        return Result<FarmTaskDto>.Success(MapTask(task));
    }

    public async Task<Result<DeltaResult<FarmTaskDto>>> GetTasksAsync(Guid farmId, FarmTaskListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        // One instant for the whole read: it is both the cursor handed back and the point the
        // delta is measured against, so a client that sends this value back cannot land on the
        // wrong side of its own answer.
        var cursor = DateTime.UtcNow;

        var query = QueryTasks(farmId);

        if (filter.UpdatedSince.HasValue)
        {
            if (!DeltaCursor.IsAnswerable(filter.UpdatedSince, cursor))
                return Result<DeltaResult<FarmTaskDto>>.Success(DeltaResult<FarmTaskDto>.FullSyncRequired(cursor));

            var since = filter.UpdatedSince.Value;

            // `COALESCE(ModifiedAt, CreatedAt)`: a task that has never been edited keeps a null
            // ModifiedAt, and asking only for ModifiedAt would leave it invisible to every
            // delta forever. The rule this encodes is "when this row last changed".
            //
            // Filters and paging are deliberately not applied: a delta answers "what changed in
            // the collection", and paging one would force the client to walk every page before
            // it could advance its cursor (see DeltaCursor.MaxDeltaRows).
            var changed = await query
                .Where(t => (t.ModifiedAt ?? t.CreatedAt) > since)
                .OrderBy(t => t.Status == FarmTaskStatus.Completed || t.Status == FarmTaskStatus.Cancelled)
                .ThenBy(t => t.DueDate)
                .ThenByDescending(t => t.Priority)
                .Take(DeltaCursor.MaxDeltaRows + 1)
                .ToListAsync();

            if (changed.Count > DeltaCursor.MaxDeltaRows)
                return Result<DeltaResult<FarmTaskDto>>.Success(DeltaResult<FarmTaskDto>.FullSyncRequired(cursor));

            var delta = new DeltaResult<FarmTaskDto>
            {
                Items = changed.Select(MapTask).ToList(),
                Page = 1,
                PageSize = changed.Count,
                TotalCount = changed.Count,
                Cursor = cursor,
                DeletedIds = await ChangeTombstones
                    .HardDeletedIdsAsync<FarmTask>(_context, farmId, since)
            };

            return Result<DeltaResult<FarmTaskDto>>.Success(delta);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(t => t.Title.Contains(filter.Search));

        if (filter.Status.HasValue)
            query = query.Where(t => t.Status == filter.Status.Value);

        if (filter.Priority.HasValue)
            query = query.Where(t => t.Priority == filter.Priority.Value);

        if (filter.AssignedEmployeeId.HasValue)
            query = query.Where(t => t.AssignedEmployeeId == filter.AssignedEmployeeId.Value);

        if (filter.AnimalId.HasValue)
            query = query.Where(t => t.AnimalId == filter.AnimalId.Value);

        if (filter.LocationId.HasValue)
            query = query.Where(t => t.LocationId == filter.LocationId.Value);

        if (filter.IncludeOverdueOnly)
            query = query.Where(t => t.DueDate < DateTime.UtcNow &&
                                     (t.Status == FarmTaskStatus.Pending || t.Status == FarmTaskStatus.InProgress));

        var full = await PageAsync(query, filter, page, pageSize);
        return Result<DeltaResult<FarmTaskDto>>.Success(DeltaResult<FarmTaskDto>.From(full, cursor));
    }

    /// <summary>One page of a task query, in the order the list is always shown in.</summary>
    private static async Task<PagedResult<FarmTaskDto>> PageAsync(
        IQueryable<FarmTask> query, FarmTaskListFilter filter, int page, int pageSize)
    {
        var totalCount = await query.CountAsync();

        var tasks = await query
            .OrderBy(t => t.Status == FarmTaskStatus.Completed || t.Status == FarmTaskStatus.Cancelled)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<FarmTaskDto>
        {
            Items = tasks.Select(MapTask).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // Commands

    public async Task<Result<FarmTaskDto>> CreateFarmTaskAsync(Guid farmId, CreateFarmTaskRequest request)
    {
        var validationError = ValidateDetails(request.Title, request.Description, request.DueDate);
        if (validationError != null)
            return Result<FarmTaskDto>.Validation(validationError);

        var referenceError = await ValidateReferencesAsync(farmId,
            request.AssignedEmployeeId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<FarmTaskDto>.NotFound(referenceError);

        var task = new FarmTask
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = request.Priority,
            Status = FarmTaskStatus.Pending,
            DueDate = request.DueDate,
            AssignedEmployeeId = request.AssignedEmployeeId,
            AnimalId = request.AnimalId,
            LocationId = request.LocationId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.FarmTasks.Add(task);
        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    public async Task<Result<FarmTaskDto>> UpdateFarmTaskAsync(Guid farmId, Guid id, UpdateFarmTaskRequest request)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        if (task.Status == FarmTaskStatus.Completed || task.Status == FarmTaskStatus.Cancelled)
            return Result<FarmTaskDto>.Conflict("Closed tasks cannot be edited. Reopen the task first.");

        var validationError = ValidateDetails(request.Title, request.Description, request.DueDate);
        if (validationError != null)
            return Result<FarmTaskDto>.Validation(validationError);

        var referenceError = await ValidateReferencesAsync(farmId,
            request.AssignedEmployeeId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<FarmTaskDto>.NotFound(referenceError);

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.Priority = request.Priority;
        task.DueDate = request.DueDate;
        task.AssignedEmployeeId = request.AssignedEmployeeId;
        task.AnimalId = request.AnimalId;
        task.LocationId = request.LocationId;
        task.ModifiedAt = DateTime.UtcNow;
        task.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    public async Task<Result> DeleteFarmTaskAsync(Guid farmId, Guid id)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result.NotFound("Task not found");

        _context.FarmTasks.Remove(task);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<FarmTaskDto>> StartTaskAsync(Guid farmId, Guid id)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        if (task.Status != FarmTaskStatus.Pending)
            return Result<FarmTaskDto>.Conflict($"Only pending tasks can be started. Current status: {task.Status}");

        task.Status = FarmTaskStatus.InProgress;
        task.StartedAt = DateTime.UtcNow;
        task.ModifiedAt = DateTime.UtcNow;
        task.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    public async Task<Result<FarmTaskDto>> CompleteTaskAsync(Guid farmId, Guid id, CompleteFarmTaskRequest request, Guid? mutationId = null)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        if (task.Status == FarmTaskStatus.Completed)
            return AlreadyCompleted(task, mutationId, request.CompletionNotes);

        if (task.Status == FarmTaskStatus.Cancelled)
            return Result<FarmTaskDto>.Conflict($"Open tasks only can be completed. Current status: {task.Status}");

        if (!string.IsNullOrWhiteSpace(request.CompletionNotes) && request.CompletionNotes.Length > 1000)
            return Result<FarmTaskDto>.Validation("Completion notes cannot exceed 1000 characters");

        var now = DateTime.UtcNow;
        var completedAt = request.OccurredAt ?? now;
        if (MutationTimestampRules.IsTooFarInTheFuture(completedAt, now))
            return Result<FarmTaskDto>.Validation(MutationTimestampRules.TaskCompletedAtMessage);

        task.Status = FarmTaskStatus.Completed;
        task.CompletedAt = completedAt;

        // The id of the queued mutation that completed this task, kept as the first one that
        // did so; a live completion leaves it null so the column only ever describes queued work.
        task.CompletionClientMutationId ??= mutationId;
        task.CompletedBy = _currentUser.GetUserId();
        task.CompletionNotes = request.CompletionNotes?.Trim();
        task.ModifiedAt = now;
        task.ModifiedBy = _currentUser.GetUserId();

        if (task.AnimalId.HasValue)
        {
            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = task.AnimalId.Value,
                FarmId = farmId,
                EventType = TimelineEventTypes.TaskCompleted,
                Title = $"Task completed: {task.Title}",
                Description = request.CompletionNotes,
                RelatedEntityId = task.Id,
                RelatedEntityType = "FarmTask",
                OccurredAt = completedAt,
                CreatedAt = now,
                CreatedBy = _currentUser.GetUserId()
            });
        }

        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    public async Task<Result<FarmTaskDto>> CancelTaskAsync(Guid farmId, Guid id, CancelFarmTaskRequest request)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        if (task.Status != FarmTaskStatus.Pending && task.Status != FarmTaskStatus.InProgress)
            return Result<FarmTaskDto>.Conflict($"Open tasks only can be cancelled. Current status: {task.Status}");

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > 1000)
            return Result<FarmTaskDto>.Validation("Cancel reason cannot exceed 1000 characters");

        var now = DateTime.UtcNow;
        task.Status = FarmTaskStatus.Cancelled;
        task.CancelReason = request.Reason?.Trim();
        task.ModifiedAt = now;
        task.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    public async Task<Result<FarmTaskDto>> ReopenTaskAsync(Guid farmId, Guid id)
    {
        var task = await _context.FarmTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FarmTaskDto>.NotFound("Task not found");

        if (task.Status != FarmTaskStatus.Completed && task.Status != FarmTaskStatus.Cancelled)
            return Result<FarmTaskDto>.Conflict($"Only completed or cancelled tasks can be reopened. Current status: {task.Status}");

        task.Status = FarmTaskStatus.Pending;
        task.StartedAt = null;
        task.CompletedAt = null;
        task.CompletedBy = null;
        task.CompletionNotes = null;
        task.CancelReason = null;
        task.ModifiedAt = DateTime.UtcNow;
        task.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await MapLoadedAsync(farmId, task.Id);
    }

    /// <summary>
    /// A completion for a task that is already complete.
    ///
    /// "Already done" is not a failure for a queued item: its intent — this task is finished —
    /// is satisfied by the state that is already there, so it is reported as
    /// <see cref="Error.SupersededCode"/> and the device may clear it. What is <em>not</em>
    /// swallowed is a disagreement about a fact: if the notes the device is reporting are not
    /// the notes already recorded, the server's record and the device's account of the work
    /// differ, and that is a conflict a human has to look at. Silently accepting it would
    /// discard what the device knew; silently overwriting it would discard what the server
    /// already recorded.
    /// </summary>
    private static Result<FarmTaskDto> AlreadyCompleted(
        FarmTask task, Guid? mutationId, string? incomingNotes)
    {
        // The same queued mutation already completed this task: a replay that arrived before
        // its ledger row existed (a crash between the apply and the record).
        if (mutationId.HasValue && task.CompletionClientMutationId == mutationId)
            return Result<FarmTaskDto>.Superseded("This completion was already applied");

        return NotesMatch(task.CompletionNotes, incomingNotes)
            ? Result<FarmTaskDto>.Superseded("Task is already completed")
            : Result<FarmTaskDto>.Conflict("This task was already completed with different completion notes");
    }

    /// <summary>
    /// Whether two completions say the same thing. Absent and blank are the same note here:
    /// neither records anything, and treating them as different would report a disagreement
    /// that does not exist.
    /// </summary>
    private static bool NotesMatch(string? stored, string? incoming) =>
        string.Equals(stored?.Trim(), incoming?.Trim(), StringComparison.Ordinal) ||
        (string.IsNullOrWhiteSpace(stored) && string.IsNullOrWhiteSpace(incoming));

    // Helpers

    private IQueryable<FarmTask> QueryTasks(Guid farmId) =>
        _context.FarmTasks
            .AsNoTracking()
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Animal)
            .Include(t => t.Location)
            .Where(t => t.FarmId == farmId);

    private async Task<FarmTask?> LoadFullAsync(Guid farmId, Guid id) =>
        await QueryTasks(farmId).FirstAsync(t => t.Id == id);

    private async Task<Result<FarmTaskDto>> MapLoadedAsync(Guid farmId, Guid taskId)
    {
        var task = await LoadFullAsync(farmId, taskId);
        return task == null
            ? Result<FarmTaskDto>.NotFound("Task not found")
            : Result<FarmTaskDto>.Success(MapTask(task));
    }

    private static string? ValidateDetails(string title, string? description, DateTime dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Title is required";
        if (title.Length > 200)
            return "Title cannot exceed 200 characters";
        if (!string.IsNullOrWhiteSpace(description) && description.Length > 2000)
            return "Description cannot exceed 2000 characters";
        return null;
    }

    private async Task<string?> ValidateReferencesAsync(Guid farmId, Guid? employeeId, Guid? animalId, Guid? locationId)
    {
        if (employeeId.HasValue &&
            !await _context.Employees.AnyAsync(e => e.Id == employeeId.Value && e.FarmId == farmId))
            return "Assigned employee not found";

        if (animalId.HasValue &&
            !await _context.Animals.AnyAsync(a => a.Id == animalId.Value && a.FarmId == farmId))
            return "Animal not found";

        if (locationId.HasValue &&
            !await _context.Locations.AnyAsync(l => l.Id == locationId.Value && l.FarmId == farmId))
            return "Location not found";

        return null;
    }

    private static FarmTaskDto MapTask(FarmTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Priority = task.Priority,
        PriorityName = task.Priority.ToString(),
        Status = task.Status,
        StatusName = task.Status.ToString(),
        DueDate = task.DueDate,
        IsOverdue = task.DueDate < DateTime.UtcNow &&
                    (task.Status == FarmTaskStatus.Pending || task.Status == FarmTaskStatus.InProgress),
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt,
        CompletionNotes = task.CompletionNotes,
        CancelReason = task.CancelReason,
        AssignedEmployeeId = task.AssignedEmployeeId,
        AssignedEmployeeName = task.AssignedEmployee == null
            ? null
            : $"{task.AssignedEmployee.FirstName} {task.AssignedEmployee.LastName}",
        AnimalId = task.AnimalId,
        AnimalName = task.Animal?.Name,
        LocationId = task.LocationId,
        LocationName = task.Location?.Name,
        CreatedAt = task.CreatedAt
    };
}
