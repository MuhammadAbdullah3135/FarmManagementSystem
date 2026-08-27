using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class MedicineService : IMedicineService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public MedicineService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // ── Medicine CRUD ──────────────────────────────────────────

    public async Task<Result<PagedResult<MedicineListItemDto>>> GetMedicinesAsync(Guid farmId, int page, int pageSize, string? search)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.Medicines
            .AsNoTracking()
            .Where(m => m.FarmId == farmId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Name.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var medicines = await query
            .OrderBy(m => m.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MedicineListItemDto
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                Unit = m.Unit,
                LowStockThreshold = m.LowStockThreshold,
                TotalQuantity = m.StockBatches.Sum(s => s.Quantity),
                BatchCount = m.StockBatches.Count
            })
            .ToListAsync();

        return Result<PagedResult<MedicineListItemDto>>.Success(new PagedResult<MedicineListItemDto>
        {
            Items = medicines,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<MedicineDto>> GetMedicineByIdAsync(Guid farmId, Guid id)
    {
        var medicine = await _context.Medicines
            .AsNoTracking()
            .Where(m => m.FarmId == farmId && m.Id == id)
            .Select(m => new MedicineDto
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                Unit = m.Unit,
                LowStockThreshold = m.LowStockThreshold,
                ExpiringSoonDays = m.ExpiringSoonDays,
                TotalQuantity = m.StockBatches.Sum(s => s.Quantity),
                BatchCount = m.StockBatches.Count,
                CreatedAt = m.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (medicine == null)
            return Result<MedicineDto>.NotFound("Medicine not found");

        return Result<MedicineDto>.Success(medicine);
    }

    public async Task<Result<MedicineDto>> CreateMedicineAsync(Guid farmId, CreateMedicineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<MedicineDto>.Validation("Medicine name is required");
        if (string.IsNullOrWhiteSpace(request.Unit))
            return Result<MedicineDto>.Validation("Unit is required");

        var exists = await _context.Medicines
            .AnyAsync(m => m.FarmId == farmId && m.Name == request.Name);
        if (exists)
            return Result<MedicineDto>.Conflict("A medicine with this name already exists");

        var userId = _currentUser.GetUserId();

        var medicine = new Medicine
        {
            FarmId = farmId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Unit = request.Unit.Trim(),
            LowStockThreshold = request.LowStockThreshold,
            ExpiringSoonDays = request.ExpiringSoonDays,
            CreatedBy = userId
        };

        _context.Medicines.Add(medicine);
        await _context.SaveChangesAsync();

        return Result<MedicineDto>.Success(new MedicineDto
        {
            Id = medicine.Id,
            Name = medicine.Name,
            Description = medicine.Description,
            Unit = medicine.Unit,
            LowStockThreshold = medicine.LowStockThreshold,
            ExpiringSoonDays = medicine.ExpiringSoonDays,
            TotalQuantity = 0,
            BatchCount = 0,
            CreatedAt = medicine.CreatedAt
        });
    }

    public async Task<Result<MedicineDto>> UpdateMedicineAsync(Guid farmId, Guid id, UpdateMedicineRequest request)
    {
        var medicine = await _context.Medicines
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == id);

        if (medicine == null)
            return Result<MedicineDto>.NotFound("Medicine not found");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<MedicineDto>.Validation("Medicine name is required");
        if (string.IsNullOrWhiteSpace(request.Unit))
            return Result<MedicineDto>.Validation("Unit is required");

        var nameExists = await _context.Medicines
            .AnyAsync(m => m.FarmId == farmId && m.Name == request.Name && m.Id != id);
        if (nameExists)
            return Result<MedicineDto>.Conflict("A medicine with this name already exists");

        var userId = _currentUser.GetUserId();

        medicine.Name = request.Name.Trim();
        medicine.Description = request.Description?.Trim();
        medicine.Unit = request.Unit.Trim();
        medicine.LowStockThreshold = request.LowStockThreshold;
        medicine.ExpiringSoonDays = request.ExpiringSoonDays;
        medicine.ModifiedAt = DateTime.UtcNow;
        medicine.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        var totalQty = await _context.MedicineStocks
            .Where(s => s.MedicineId == id)
            .SumAsync(s => s.Quantity);

        var batchCount = await _context.MedicineStocks
            .CountAsync(s => s.MedicineId == id);

        return Result<MedicineDto>.Success(new MedicineDto
        {
            Id = medicine.Id,
            Name = medicine.Name,
            Description = medicine.Description,
            Unit = medicine.Unit,
            LowStockThreshold = medicine.LowStockThreshold,
            ExpiringSoonDays = medicine.ExpiringSoonDays,
            TotalQuantity = totalQty,
            BatchCount = batchCount,
            CreatedAt = medicine.CreatedAt
        });
    }

    public async Task<Result> DeleteMedicineAsync(Guid farmId, Guid id)
    {
        var medicine = await _context.Medicines
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == id);

        if (medicine == null)
            return Result.NotFound("Medicine not found");

        var hasUsages = await _context.MedicineUsages.AnyAsync(u => u.MedicineId == id);
        if (hasUsages)
            return Result.Validation("Cannot delete medicine with existing usage records");

        _context.Medicines.Remove(medicine);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── Stock Batch Management ─────────────────────────────────

    public async Task<Result<List<MedicineStockDto>>> GetStockBatchesAsync(Guid farmId, Guid medicineId)
    {
        var medicine = await _context.Medicines
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == medicineId);
        if (medicine == null)
            return Result<List<MedicineStockDto>>.NotFound("Medicine not found");

        var batches = await _context.MedicineStocks
            .AsNoTracking()
            .Where(s => s.MedicineId == medicineId)
            .OrderBy(s => s.ExpiryDate)
            .Select(s => new MedicineStockDto
            {
                Id = s.Id,
                MedicineId = s.MedicineId,
                BatchNumber = s.BatchNumber,
                Quantity = s.Quantity,
                UnitCost = s.UnitCost,
                ExpiryDate = s.ExpiryDate,
                Supplier = s.Supplier,
                DateReceived = s.DateReceived
            })
            .ToListAsync();

        return Result<List<MedicineStockDto>>.Success(batches);
    }

    public async Task<Result<MedicineStockDto>> AddStockBatchAsync(Guid farmId, Guid medicineId, AddStockBatchRequest request)
    {
        var medicine = await _context.Medicines
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == medicineId);
        if (medicine == null)
            return Result<MedicineStockDto>.NotFound("Medicine not found");

        if (string.IsNullOrWhiteSpace(request.BatchNumber))
            return Result<MedicineStockDto>.Validation("Batch number is required");
        if (request.Quantity <= 0)
            return Result<MedicineStockDto>.Validation("Quantity must be greater than zero");

        var userId = _currentUser.GetUserId();

        var stock = new MedicineStock
        {
            FarmId = farmId,
            MedicineId = medicineId,
            BatchNumber = request.BatchNumber.Trim(),
            Quantity = request.Quantity,
            UnitCost = request.UnitCost,
            ExpiryDate = request.ExpiryDate,
            Supplier = request.Supplier?.Trim(),
            DateReceived = request.DateReceived ?? DateTime.UtcNow,
            CreatedBy = userId
        };

        _context.MedicineStocks.Add(stock);
        await _context.SaveChangesAsync();

        return Result<MedicineStockDto>.Success(new MedicineStockDto
        {
            Id = stock.Id,
            MedicineId = stock.MedicineId,
            BatchNumber = stock.BatchNumber,
            Quantity = stock.Quantity,
            UnitCost = stock.UnitCost,
            ExpiryDate = stock.ExpiryDate,
            Supplier = stock.Supplier,
            DateReceived = stock.DateReceived
        });
    }

    public async Task<Result> DeleteStockBatchAsync(Guid farmId, Guid medicineId, Guid stockId)
    {
        var stock = await _context.MedicineStocks
            .FirstOrDefaultAsync(s => s.Id == stockId && s.MedicineId == medicineId);
        if (stock == null)
            return Result.NotFound("Stock batch not found");

        var hasUsages = await _context.MedicineUsages.AnyAsync(u => u.MedicineStockId == stockId);
        if (hasUsages)
            return Result.Validation("Cannot delete stock batch with existing usage records");

        _context.MedicineStocks.Remove(stock);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── Usage Recording (FIFO) ─────────────────────────────────

    public async Task<Result<MedicineUsageDto>> RecordUsageAsync(Guid farmId, RecordMedicineUsageRequest request)
    {
        var medicine = await _context.Medicines
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.Id == request.MedicineId);
        if (medicine == null)
            return Result<MedicineUsageDto>.NotFound("Medicine not found");

        if (request.QuantityUsed <= 0)
            return Result<MedicineUsageDto>.Validation("Quantity must be greater than zero");

        // Get available batches ordered by expiry (FIFO — nearest expiry first)
        var batches = await _context.MedicineStocks
            .Where(s => s.MedicineId == request.MedicineId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync();

        var totalAvailable = batches.Sum(b => b.Quantity);
        if (totalAvailable < request.QuantityUsed)
            return Result<MedicineUsageDto>.Validation(
                $"Insufficient stock. Available: {totalAvailable} {medicine.Unit}, requested: {request.QuantityUsed}");

        var userId = _currentUser.GetUserId();
        var remaining = request.QuantityUsed;
        var dateUsed = request.DateUsed ?? DateTime.UtcNow;
        MedicineStock? lastDeductedBatch = null;

        foreach (var batch in batches)
        {
            if (remaining <= 0) break;

            var deduct = Math.Min(batch.Quantity, remaining);
            batch.Quantity -= deduct;
            remaining -= deduct;
            lastDeductedBatch = batch;
        }

        // Create a single usage record linked to the last batch deducted
        var usage = new MedicineUsage
        {
            FarmId = farmId,
            MedicineId = request.MedicineId,
            MedicineStockId = lastDeductedBatch!.Id,
            MedicalRecordId = request.MedicalRecordId,
            QuantityUsed = request.QuantityUsed,
            DateUsed = dateUsed,
            Notes = request.Notes?.Trim(),
            CreatedBy = userId
        };

        _context.MedicineUsages.Add(usage);
        await _context.SaveChangesAsync();

        return Result<MedicineUsageDto>.Success(new MedicineUsageDto
        {
            Id = usage.Id,
            MedicineId = usage.MedicineId,
            MedicineName = medicine.Name,
            MedicineStockId = usage.MedicineStockId,
            BatchNumber = lastDeductedBatch.BatchNumber,
            MedicalRecordId = usage.MedicalRecordId,
            QuantityUsed = usage.QuantityUsed,
            DateUsed = usage.DateUsed,
            Notes = usage.Notes
        });
    }

    // ── Alerts ─────────────────────────────────────────────────

    public async Task<Result<List<MedicineAlertDto>>> GetAlertsAsync(Guid farmId)
    {
        var medicines = await _context.Medicines
            .AsNoTracking()
            .Where(m => m.FarmId == farmId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var alerts = new List<MedicineAlertDto>();

        foreach (var medicine in medicines)
        {
            var batches = await _context.MedicineStocks
                .AsNoTracking()
                .Where(s => s.MedicineId == medicine.Id && s.Quantity > 0)
                .ToListAsync();

            foreach (var batch in batches)
            {
                // Expired
                if (batch.ExpiryDate < now)
                {
                    alerts.Add(new MedicineAlertDto
                    {
                        MedicineId = medicine.Id,
                        MedicineName = medicine.Name,
                        Unit = medicine.Unit,
                        StockId = batch.Id,
                        BatchNumber = batch.BatchNumber,
                        CurrentQuantity = batch.Quantity,
                        LowStockThreshold = medicine.LowStockThreshold,
                        ExpiryDate = batch.ExpiryDate,
                        AlertType = MedicineAlertType.Expired,
                        AlertTypeName = "Expired"
                    });
                }
                // Expiring soon
                else if (batch.ExpiryDate <= now.AddDays(medicine.ExpiringSoonDays))
                {
                    alerts.Add(new MedicineAlertDto
                    {
                        MedicineId = medicine.Id,
                        MedicineName = medicine.Name,
                        Unit = medicine.Unit,
                        StockId = batch.Id,
                        BatchNumber = batch.BatchNumber,
                        CurrentQuantity = batch.Quantity,
                        LowStockThreshold = medicine.LowStockThreshold,
                        ExpiryDate = batch.ExpiryDate,
                        AlertType = MedicineAlertType.ExpiringSoon,
                        AlertTypeName = "Expiring Soon"
                    });
                }
            }

            // Low stock (total across all batches)
            var totalQty = batches.Sum(b => b.Quantity);
            if (totalQty < medicine.LowStockThreshold)
            {
                alerts.Add(new MedicineAlertDto
                {
                    MedicineId = medicine.Id,
                    MedicineName = medicine.Name,
                    Unit = medicine.Unit,
                    StockId = Guid.Empty,
                    BatchNumber = "-",
                    CurrentQuantity = totalQty,
                    LowStockThreshold = medicine.LowStockThreshold,
                    ExpiryDate = now,
                    AlertType = MedicineAlertType.LowStock,
                    AlertTypeName = "Low Stock"
                });
            }
        }

        // Sort: Expired first, then ExpiringSoon, then LowStock
        alerts = alerts
            .OrderBy(a => a.AlertType)
            .ThenBy(a => a.ExpiryDate)
            .ToList();

        return Result<List<MedicineAlertDto>>.Success(alerts);
    }
}
