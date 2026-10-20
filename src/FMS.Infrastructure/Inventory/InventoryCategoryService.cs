using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Inventory;

public class InventoryCategoryService : IInventoryCategoryService
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public InventoryCategoryService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<List<InventoryCategoryDto>>> GetCategoriesAsync(Guid farmId, bool includeInactive)
    {
        var query = _context.InventoryCategories.AsNoTracking().Where(c => c.FarmId == farmId);
        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        var categories = await query
            .OrderBy(c => c.Name)
            .Select(c => new InventoryCategoryDto
            {
                Id = c.Id,
                FarmId = c.FarmId,
                Name = c.Name,
                Description = c.Description,
                IsActive = c.IsActive,
                ItemCount = _context.InventoryItems.Count(i => i.CategoryId == c.Id),
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        return Result<List<InventoryCategoryDto>>.Success(categories);
    }

    public async Task<Result<InventoryCategoryDto>> GetCategoryByIdAsync(Guid farmId, Guid id)
    {
        var category = await _context.InventoryCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == id);
        return category == null
            ? Result<InventoryCategoryDto>.NotFound(
                "Inventory category not found", DomainMessageKeys.InventoryCategoryNotFound)
            : Result<InventoryCategoryDto>.Success(ToDto(category, 0));
    }

    public async Task<Result<InventoryCategoryDto>> CreateCategoryAsync(
        Guid farmId, CreateInventoryCategoryRequest request)
    {
        var error = Validate(request.Name, request.Description);
        if (error != null)
            return Result<InventoryCategoryDto>.Validation(error.Message, error.MessageKey, error.MessageArgs);

        var name = request.Name.Trim();
        if (await NameExistsAsync(farmId, name, null))
            return Duplicate(name);

        var category = new InventoryCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = name,
            Description = Clean(request.Description),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.InventoryCategories.Add(category);
        await _context.SaveChangesAsync();
        return Result<InventoryCategoryDto>.Success(ToDto(category, 0));
    }

    public async Task<Result<InventoryCategoryDto>> UpdateCategoryAsync(
        Guid farmId, Guid id, UpdateInventoryCategoryRequest request)
    {
        var category = await _context.InventoryCategories
            .FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == id);
        if (category == null)
            return Result<InventoryCategoryDto>.NotFound(
                "Inventory category not found", DomainMessageKeys.InventoryCategoryNotFound);

        var error = Validate(request.Name, request.Description);
        if (error != null)
            return Result<InventoryCategoryDto>.Validation(error.Message, error.MessageKey, error.MessageArgs);

        var name = request.Name.Trim();
        if (await NameExistsAsync(farmId, name, id))
            return Duplicate(name);

        var renamed = !string.Equals(category.Name, name, StringComparison.Ordinal);
        category.Name = name;
        category.Description = Clean(request.Description);
        category.IsActive = request.IsActive;
        category.ModifiedAt = DateTime.UtcNow;
        category.ModifiedBy = _currentUser.GetUserId();

        // The retained string is still the fallback for clients that have not moved to
        // CategoryId yet, so a rename must not leave linked items showing the old name.
        if (renamed)
        {
            var linkedItems = await _context.InventoryItems
                .Where(i => i.FarmId == farmId && i.CategoryId == id)
                .ToListAsync();
            foreach (var item in linkedItems)
                item.Category = name;
        }

        await _context.SaveChangesAsync();
        var itemCount = await _context.InventoryItems.CountAsync(i => i.CategoryId == id);
        return Result<InventoryCategoryDto>.Success(ToDto(category, itemCount));
    }

    public async Task<Result> DeleteCategoryAsync(Guid farmId, Guid id)
    {
        var category = await _context.InventoryCategories
            .FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == id);
        if (category == null)
            return Result.NotFound("Inventory category not found", DomainMessageKeys.InventoryCategoryNotFound);

        if (await _context.InventoryItems.AnyAsync(i => i.CategoryId == id))
            return Result.Conflict(
                "Cannot delete an inventory category that has items; deactivate it instead",
                DomainMessageKeys.InventoryCategoryHasItems);

        _context.InventoryCategories.Remove(category);
        await _context.SaveChangesAsync();
        return Result.Success();
    }

    private Task<bool> NameExistsAsync(Guid farmId, string name, Guid? exceptId)
    {
        var normalized = name.ToLower();
        return _context.InventoryCategories.AnyAsync(c =>
            c.FarmId == farmId
            && c.Id != exceptId
            && c.Name.ToLower() == normalized);
    }

    private static Result<InventoryCategoryDto> Duplicate(string name) =>
        Result<InventoryCategoryDto>.Conflict(
            $"An inventory category named '{name}' already exists",
            DomainMessageKeys.InventoryCategoryNameExists,
            new Dictionary<string, object?> { ["name"] = name });

    private static Error? Validate(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation(
                "Category name is required", DomainMessageKeys.InventoryCategoryNameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
            return Error.Validation(
                $"Category name cannot exceed {NameMaxLength} characters",
                DomainMessageKeys.InventoryCategoryNameMaxLength,
                new Dictionary<string, object?> { ["max"] = NameMaxLength });

        if (description is not null && description.Trim().Length > DescriptionMaxLength)
            return Error.Validation(
                $"Description cannot exceed {DescriptionMaxLength} characters",
                DomainMessageKeys.InventoryCategoryDescriptionMaxLength,
                new Dictionary<string, object?> { ["max"] = DescriptionMaxLength });

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InventoryCategoryDto ToDto(InventoryCategory category, int itemCount) => new()
    {
        Id = category.Id,
        FarmId = category.FarmId,
        Name = category.Name,
        Description = category.Description,
        IsActive = category.IsActive,
        ItemCount = itemCount,
        CreatedAt = category.CreatedAt
    };
}
