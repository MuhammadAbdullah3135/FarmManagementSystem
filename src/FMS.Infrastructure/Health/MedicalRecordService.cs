using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class MedicalRecordService : IMedicalRecordService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public MedicalRecordService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<MedicalRecordListItemDto>>> GetMedicalRecordsAsync(Guid farmId, MedicalRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.MedicalRecords
            .AsNoTracking()
            .Where(m => m.FarmId == farmId);

        if (filter.AnimalId.HasValue)
            query = query.Where(m => m.AnimalId == filter.AnimalId.Value);

        if (filter.Status.HasValue)
            query = query.Where(m => m.Status == filter.Status.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(m => m.DateRecorded >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(m => m.DateRecorded <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(m =>
                m.Animal.TagNumber.Contains(term) ||
                (m.Animal.Name != null && m.Animal.Name.Contains(term)) ||
                (m.Diagnosis != null && m.Diagnosis.Contains(term)) ||
                (m.VetName != null && m.VetName.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(m => m.DateRecorded)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MedicalRecordListItemDto
            {
                Id = m.Id,
                AnimalId = m.AnimalId,
                AnimalTagNumber = m.Animal.TagNumber,
                AnimalName = m.Animal.Name,
                Diagnosis = m.Diagnosis,
                VetName = m.VetName,
                Cost = m.Cost,
                DateRecorded = m.DateRecorded,
                FollowUpDate = m.FollowUpDate,
                Status = m.Status,
                StatusName = m.Status.ToString()
            })
            .ToListAsync();

        return Result<PagedResult<MedicalRecordListItemDto>>.Success(new PagedResult<MedicalRecordListItemDto>
        {
            Items = records,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<MedicalRecordDto>> GetMedicalRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.MedicalRecords
            .AsNoTracking()
            .Where(m => m.FarmId == farmId && m.Id == id)
            .Include(m => m.Animal)
            .FirstOrDefaultAsync();

        if (record == null)
            return Result<MedicalRecordDto>.NotFound("Medical record not found");

        return Result<MedicalRecordDto>.Success(MapToDto(record));
    }

    public async Task<Result<PagedResult<MedicalRecordListItemDto>>> GetMedicalRecordsByAnimalAsync(Guid farmId, Guid animalId, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.MedicalRecords
            .AsNoTracking()
            .Where(m => m.FarmId == farmId && m.AnimalId == animalId);

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(m => m.DateRecorded)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MedicalRecordListItemDto
            {
                Id = m.Id,
                AnimalId = m.AnimalId,
                AnimalTagNumber = m.Animal.TagNumber,
                AnimalName = m.Animal.Name,
                Diagnosis = m.Diagnosis,
                VetName = m.VetName,
                Cost = m.Cost,
                DateRecorded = m.DateRecorded,
                FollowUpDate = m.FollowUpDate,
                Status = m.Status,
                StatusName = m.Status.ToString()
            })
            .ToListAsync();

        return Result<PagedResult<MedicalRecordListItemDto>>.Success(new PagedResult<MedicalRecordListItemDto>
        {
            Items = records,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<MedicalRecordDto>> CreateMedicalRecordAsync(Guid farmId, CreateMedicalRecordRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.AnimalId && a.FarmId == farmId && !a.IsDeleted);

        if (animal == null)
            return Result<MedicalRecordDto>.Validation("Animal not found in this farm");

        if (string.IsNullOrWhiteSpace(request.Symptoms))
            return Result<MedicalRecordDto>.Validation("Symptoms are required");

        var userId = _currentUser.GetUserId();

        var record = new MedicalRecord
        {
            FarmId = farmId,
            AnimalId = request.AnimalId,
            Symptoms = request.Symptoms.Trim(),
            Diagnosis = request.Diagnosis?.Trim(),
            Treatment = request.Treatment?.Trim(),
            MedicineUsed = request.MedicineUsed?.Trim(),
            Dosage = request.Dosage?.Trim(),
            VetName = request.VetName?.Trim(),
            Cost = request.Cost,
            DateRecorded = request.DateRecorded == default ? DateTime.UtcNow : request.DateRecorded,
            FollowUpDate = request.FollowUpDate,
            Status = request.Status,
            Notes = request.Notes?.Trim(),
            CreatedBy = userId
        };

        _context.MedicalRecords.Add(record);

        var timelineEvent = new AnimalTimelineEvent
        {
            AnimalId = request.AnimalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.MedicalRecord,
            Title = $"Medical record: {request.Diagnosis ?? request.Symptoms}",
            Description = request.Treatment,
            RelatedEntityId = record.Id,
            RelatedEntityType = nameof(MedicalRecord),
            OccurredAt = record.DateRecorded,
            CreatedBy = userId
        };
        _context.AnimalTimelineEvents.Add(timelineEvent);

        // Auto-generate follow-up task if FollowUpDate is set
        if (request.FollowUpDate.HasValue && request.FollowUpDate.Value > DateTime.UtcNow)
        {
            var followUpTask = new FarmTask
            {
                FarmId = farmId,
                Title = $"Follow-up: {request.Diagnosis ?? request.Symptoms} \u2014 {animal.TagNumber}",
                Description = $"Follow-up for medical record. Vet: {request.VetName ?? "TBD"}",
                Priority = FarmTaskPriority.Medium,
                Status = FarmTaskStatus.Pending,
                DueDate = request.FollowUpDate.Value,
                AnimalId = request.AnimalId,
                CreatedBy = userId
            };
            _context.FarmTasks.Add(followUpTask);
            await _context.SaveChangesAsync();
            record.FollowUpTaskId = followUpTask.Id;
            await _context.SaveChangesAsync();
        }
        else
        {
            await _context.SaveChangesAsync();
        }

        // Auto-create Expense when Cost > 0
        if (request.Cost > 0)
        {
            var expense = await AutoCreateVetExpenseAsync(farmId, request.Cost, request.VetName,
                $"Medical: {request.Diagnosis ?? request.Symptoms}", request.DateRecorded == default ? DateTime.UtcNow : request.DateRecorded, userId);
            record.ExpenseId = expense.Id;
            await _context.SaveChangesAsync();
        }

        var created = await _context.MedicalRecords
            .Include(m => m.Animal)
            .FirstAsync(m => m.Id == record.Id);

        return Result<MedicalRecordDto>.Success(MapToDto(created));
    }

    public async Task<Result<MedicalRecordDto>> UpdateMedicalRecordAsync(Guid farmId, Guid id, UpdateMedicalRecordRequest request)
    {
        var record = await _context.MedicalRecords
            .Where(m => m.FarmId == farmId && m.Id == id)
            .Include(m => m.Animal)
            .FirstOrDefaultAsync();

        if (record == null)
            return Result<MedicalRecordDto>.NotFound("Medical record not found");

        if (string.IsNullOrWhiteSpace(request.Symptoms))
            return Result<MedicalRecordDto>.Validation("Symptoms are required");

        if (record.AnimalId != request.AnimalId)
        {
            var animal = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.AnimalId && a.FarmId == farmId && !a.IsDeleted);
            if (animal == null)
                return Result<MedicalRecordDto>.Validation("Animal not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        record.AnimalId = request.AnimalId;
        record.Symptoms = request.Symptoms.Trim();
        record.Diagnosis = request.Diagnosis?.Trim();
        record.Treatment = request.Treatment?.Trim();
        record.MedicineUsed = request.MedicineUsed?.Trim();
        record.Dosage = request.Dosage?.Trim();
        record.VetName = request.VetName?.Trim();
        record.Cost = request.Cost;
        record.DateRecorded = request.DateRecorded;
        record.FollowUpDate = request.FollowUpDate;
        record.Status = request.Status;
        record.Notes = request.Notes?.Trim();
        record.ModifiedAt = DateTime.UtcNow;
        record.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        var updated = await _context.MedicalRecords
            .Include(m => m.Animal)
            .FirstAsync(m => m.Id == id);

        return Result<MedicalRecordDto>.Success(MapToDto(updated));
    }

    public async Task<Result> DeleteMedicalRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.MedicalRecords
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == id);

        if (record == null)
            return Result.NotFound("Medical record not found");

        _context.MedicalRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    private async Task<Expense> AutoCreateVetExpenseAsync(Guid farmId, decimal amount, string? vetName, string description, DateTime expenseDate, Guid? userId)
    {
        // Find or create a "Veterinary" expense category
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == "Veterinary");
        if (category == null)
        {
            category = new ExpenseCategory
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = "Veterinary",
                Description = "Auto-created for health records",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            _context.ExpenseCategories.Add(category);
        }

        // Find or create a default "Cash" payment method
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(p => p.FarmId == farmId && p.Name == "Cash");
        if (paymentMethod == null)
        {
            paymentMethod = new PaymentMethod
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = "Cash",
                Description = "Default payment method",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            _context.PaymentMethods.Add(paymentMethod);
        }

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ExpenseDate = expenseDate,
            Amount = Math.Round(amount, 2),
            ExpenseCategoryId = category.Id,
            PaymentMethodId = paymentMethod.Id,
            Description = $"{description} (Vet: {vetName ?? "N/A"})",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        return expense;
    }

    private static MedicalRecordDto MapToDto(MedicalRecord record)
    {
        return new MedicalRecordDto
        {
            Id = record.Id,
            FarmId = record.FarmId,
            AnimalId = record.AnimalId,
            AnimalTagNumber = record.Animal.TagNumber,
            AnimalName = record.Animal.Name,
            Symptoms = record.Symptoms,
            Diagnosis = record.Diagnosis,
            Treatment = record.Treatment,
            MedicineUsed = record.MedicineUsed,
            Dosage = record.Dosage,
            VetName = record.VetName,
            Cost = record.Cost,
            DateRecorded = record.DateRecorded,
            FollowUpDate = record.FollowUpDate,
            Status = record.Status,
            StatusName = record.Status.ToString(),
            Notes = record.Notes,
            FollowUpTaskId = record.FollowUpTaskId,
            CreatedAt = record.CreatedAt
        };
    }
}
