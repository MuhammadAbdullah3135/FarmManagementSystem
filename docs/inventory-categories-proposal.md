# Inventory categories — proposal

Status: **awaiting approval**. No entity, migration, endpoint, or UI change has been
created. This document is the decision package for the first Bucket 3 master list.

## Why this list first

`InventoryItem.Category` is currently a free-text string
(`src/FMS.Domain/Entities/InventoryItem.cs`). It is the smallest Bucket 3 list:

- It has one consumer (the inventory item form and its table/filter).
- It has no clinical, medicine-batch, or report-grouping overlap.
- Existing values can be backfilled from one column without joining several records.
- It proves the master-list pattern before the larger vet and batch decisions.

The field remains optional. Items without a category stay valid.

## Entity

Owner: **Inventory** module (`FMS.Domain` entity, `FMS.Infrastructure` service,
`FMS.API` controller, `client/src/pages/inventory`).

```text
InventoryCategory : AuditableEntity
  Id             Guid, PK
  FarmId         Guid, FK -> Farms, required, indexed
  Name           string(100), required
  Description    string(500), optional
  IsActive       bool, default true
  CreatedAt      DateTime
  CreatedBy      Guid?
  ModifiedAt     DateTime?
  ModifiedBy     Guid?

Indexes:
  unique (FarmId, Name)
  index  (FarmId, IsActive)
```

Rules:

- `Name` is trimmed before validation and storage; empty names are rejected.
- Names are unique per farm. The first implementation uses the database's normal
  collation; if case-insensitive uniqueness is required, normalise the comparison in the
  service and add a functional/unique index in the same migration.
- Deleting a category that is still referenced is blocked with a validation message.
  Callers can deactivate it instead, so existing items keep a readable category.
- `IsActive=false` hides a category from new selections but leaves it visible on existing
  items and filters.

### Transitional column on InventoryItem

```text
InventoryItem
  CategoryId     Guid?, FK -> InventoryCategories, optional, indexed, OnDelete: SetNull
  Category       string?, retained for at least one release
```

The existing `Category` string is **not removed in the first migration**. During the
transition the API keeps returning both values, and the item form writes the selected
category's name into `Category` as well as its id into `CategoryId`. Reads prefer the
joined category name and fall back to the old string. A later migration can drop
`Category` once every client writes `CategoryId`.

## Migration steps

One migration, for example `AddInventoryCategories`:

1. Create `InventoryCategories` with the columns and indexes above.
2. Add nullable `CategoryId` to `InventoryItems`, the foreign key, and its index.
3. Backfill one row per distinct non-empty `(FarmId, trim(Category))` into
   `InventoryCategories`. Suggested SQL shape:

   ```sql
   INSERT INTO "InventoryCategories" ("Id", "FarmId", "Name", "IsActive", "CreatedAt")
   SELECT gen_random_uuid(), "FarmId", MIN(trim("Category")), true, now()
   FROM "InventoryItems"
   WHERE "Category" IS NOT NULL AND trim("Category") <> ''
   GROUP BY "FarmId", lower(trim("Category"));
   ```

   `lower(...)` in the grouping is what prevents two case variants becoming two
   categories if the chosen uniqueness rule is case-insensitive.
4. Match each item back to the inserted row and set `CategoryId`. Items with a blank or
   null category remain `null`.
5. Leave `Category` populated. The down migration only drops `CategoryId` and the new
   table; it does not touch the retained strings.

No production migration is run as part of this proposal. Approval is required before
creating the entity or migration.

## API endpoints

Base route follows the existing inventory controller style:
`api/farm/{farmId:guid}/inventory/categories`.

| Method | Route | Purpose | Access |
|--------|-------|---------|--------|
| `GET` | `/inventory/categories?includeInactive=false` | List the farm's categories, ordered by name | Same inventory read roles: `Employee`, `Veterinarian`, `FarmManager`, `SystemOwner` |
| `GET` | `/inventory/categories/{id:guid}` | Read one category | Same as list |
| `POST` | `/inventory/categories` | Create `{ name, description? }` | `FarmManager`, `SystemOwner` |
| `PUT` | `/inventory/categories/{id:guid}` | Rename, edit description, or toggle `isActive` | `FarmManager`, `SystemOwner` |
| `DELETE` | `/inventory/categories/{id:guid}` | Delete only when unused; otherwise `409` with a "deactivate instead" message | `FarmManager`, `SystemOwner` |

Every handler is farm-scoped in the query (`c.FarmId == farmId`); the existing
`FarmContextMiddleware` continues to validate membership and the caller's farm role.
The inventory-item list/detail/create/update responses gain `categoryId` alongside the
existing `category` string. That DTO addition happens only after approval.

## UI change

1. **Management screen:** Inventory → Categories (or Configuration → Inventory
   categories). A table with name, description, active state, item count, and edit /
   deactivate actions. Add/edit uses a small modal with Name, Description, and Active.
2. **Inventory item form:** replace the free-text `Category` input with a category
   picker. Only active categories are offered for new/edited items, plus the item's
   current inactive category when editing an old row. A "Manage categories" link opens the
   management screen. Category stays optional.
3. **Inventory item table/filter:** show the joined category name; filter by category id
   rather than by the old string. Legacy rows without `CategoryId` continue to show the
   retained string until backfill or edit.
4. **Localisation:** add keys for the screen, empty state, validation, delete-blocked
   message, and active/inactive labels in `en`, `es`, and `ar`. Do not leave English
   literals in the components.
5. **No report or grouping change:** inventory stock value and movement calculations do
   not change. Category remains descriptive metadata.

## Estimated files touched

Approximately **18–22 files**, including tests and the migration:

- `src/FMS.Domain/Entities/InventoryCategory.cs` (new)
- `src/FMS.Domain/Entities/InventoryItem.cs`
- `src/FMS.Infrastructure/Persistence/FmsDbContext.cs`
- `src/FMS.Infrastructure/Persistence/Configurations/InventoryCategoryConfiguration.cs` (new)
- `src/FMS.Infrastructure/Persistence/Configurations/InventoryItemConfiguration.cs`
- `src/FMS.Infrastructure/Persistence/Migrations/<timestamp>_AddInventoryCategories.cs` (new)
- `src/FMS.Infrastructure/Persistence/Migrations/FmsDbContextModelSnapshot.cs`
- `src/FMS.Application/Inventory/InventoryDtos.cs`
- `src/FMS.Application/Inventory/IInventoryService.cs`
- `src/FMS.Application/Inventory/InventoryCategoryDtos.cs` (new, or one DTO file)
- `src/FMS.Infrastructure/Inventory/InventoryService.cs`
- `src/FMS.Infrastructure/Inventory/InventoryCategoryService.cs` (new) and its interface
- `src/FMS.API/Controllers/InventoryCategoriesController.cs` (new)
- `src/FMS.API/Program.cs`
- `tests/FMS.Domain.Tests/Inventory/InventoryCategoryServiceTests.cs` (new)
- `tests/FMS.Domain.Tests/E2E/InventoryCategoriesE2ETests.cs` (new)
- `client/src/types/index.ts`
- `client/src/api/inventory.ts`
- `client/src/pages/inventory/InventoryCategoriesPage.tsx` (new)
- `client/src/pages/inventory/InventoryItemsPage.tsx`
- `client/src/App.tsx`
- locale files (`en`/`es`/`ar` inventory JSON)

## Decisions needed before implementation

1. Approve the entity fields and the per-farm unique name rule.
2. Approve whether category write actions are limited to `FarmManager`/`SystemOwner`
   while reads follow the inventory list roles.
3. Approve the transitional approach: keep `Category` for one release and double-write
   `CategoryId` + `Category`, rather than dropping the string immediately.
4. Approve running the migration and backfill in a controlled environment. Production
   data must not be migrated without explicit approval.
