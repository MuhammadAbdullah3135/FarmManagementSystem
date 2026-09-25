using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using FMS.Domain.Enums;

namespace FMS.Infrastructure.Inventory;

/// <summary>
/// The rules an inventory item must satisfy, in one place because two paths check
/// them: <see cref="InventoryService.CreateItemAsync"/> (one item, the endpoint a user
/// sees) and the bulk import (many items, the same rules per row).
///
/// Keeping them here is what makes the bulk path's messages byte-identical to the
/// endpoint's rather than a second, drifting copy of the same sentences.
///
/// <para>
/// The maximum lengths are the EF configuration's, and they are enforced here rather
/// than left to the database. PostgreSQL rejects an over-long value with an exception,
/// which for a bulk import means a 500 in the middle of a batch instead of a row the
/// user can fix, and for the endpoint means a 500 instead of a validation message.
/// </para>
/// </summary>
public static class InventoryItemRules
{
    public const int NameMaxLength = 200;
    public const int UnitMaxLength = 50;
    public const int CategoryMaxLength = 100;
    public const int LocationMaxLength = 200;

    /// <summary>
    /// The name-conflict message, unchanged from the create endpoint. A name is the
    /// item's identifier: it is what the unique index on (FarmId, Name) enforces, so a
    /// duplicate is an error rather than something to skip or overwrite.
    /// </summary>
    public const string DuplicateNameMessage = "An inventory item with this name already exists";

    /// <summary>
    /// Every field-level problem with one item, in the order the create endpoint has
    /// always checked them, so its first-message answer is unchanged for every input it
    /// already rejected.
    /// </summary>
    public static List<FieldError> Validate(
        string? name,
        string? category,
        string? unit,
        decimal quantity,
        decimal reorderLevel,
        decimal unitCost,
        string? location,
        bool validateQuantity = true)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError(InventoryImportFields.Name, "Item name is required", "validation.inventoryItem.nameRequired"));
        else if (name.Trim().Length > NameMaxLength)
            errors.Add(new FieldError(
                InventoryImportFields.Name,
                $"Item name cannot exceed {NameMaxLength} characters",
                "validation.inventoryItem.nameMaxLength",
                new Dictionary<string, object?> { ["max"] = NameMaxLength }));

        if (string.IsNullOrWhiteSpace(unit))
            errors.Add(new FieldError(InventoryImportFields.Unit, "Unit is required", "validation.inventoryItem.unitRequired"));
        else if (unit.Trim().Length > UnitMaxLength)
            errors.Add(new FieldError(
                InventoryImportFields.Unit,
                $"Unit cannot exceed {UnitMaxLength} characters",
                "validation.inventoryItem.unitMaxLength",
                new Dictionary<string, object?> { ["max"] = UnitMaxLength }));

        if (validateQuantity && quantity < 0)
            errors.Add(new FieldError(InventoryImportFields.Quantity, "Quantity cannot be negative", "validation.inventoryItem.quantityNegative"));

        if (reorderLevel < 0)
            errors.Add(new FieldError(InventoryImportFields.ReorderLevel, "Reorder level cannot be negative", "validation.inventoryItem.reorderLevelNegative"));

        if (unitCost < 0)
            errors.Add(new FieldError(InventoryImportFields.UnitCost, "Unit cost cannot be negative", "validation.inventoryItem.unitCostNegative"));

        if (category is not null && category.Trim().Length > CategoryMaxLength)
            errors.Add(new FieldError(
                InventoryImportFields.Category,
                $"Category cannot exceed {CategoryMaxLength} characters",
                "validation.inventoryItem.categoryMaxLength",
                new Dictionary<string, object?> { ["max"] = CategoryMaxLength }));

        if (location is not null && location.Trim().Length > LocationMaxLength)
            errors.Add(new FieldError(
                InventoryImportFields.Location,
                $"Location cannot exceed {LocationMaxLength} characters",
                "validation.inventoryItem.locationMaxLength",
                new Dictionary<string, object?> { ["max"] = LocationMaxLength }));

        return errors;
    }

    /// <summary>
    /// Builds the item exactly as the create endpoint does, including its opening-stock
    /// movement, so an imported item and a hand-entered one are the same record — and
    /// an imported quantity shows up in the movement ledger with the same reason as a
    /// manually entered one.
    /// </summary>
    public static InventoryItem Build(
        Guid farmId,
        CreateInventoryItemRequest request,
        Guid? userId,
        DateTime? movementDate = null)
    {
        var item = new InventoryItem
        {
            FarmId = farmId,
            Name = request.Name.Trim(),
            Category = Clean(request.Category),
            Unit = request.Unit.Trim(),
            Quantity = 0,
            ReorderLevel = request.ReorderLevel,
            UnitCost = request.UnitCost,
            Location = Clean(request.Location),
            CreatedBy = userId
        };

        if (request.Quantity > 0)
        {
            item.StockMovements.Add(new StockMovement
            {
                FarmId = farmId,
                MovementType = InventoryMovementType.Purchase,
                Quantity = request.Quantity,
                MovementDate = movementDate ?? DateTime.UtcNow,
                Reason = "Opening stock",
                PerformedBy = userId,
                CreatedBy = userId
            });

            item.Quantity = request.Quantity;
        }

        return item;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
