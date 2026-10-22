# Deployment checklist — Phase D.2 pre-deployment readiness

Status: **documentation only — not executed.** No production database was contacted, no
migration was applied, and no deployment was performed while preparing this checklist.

Baseline commit: `e6c4958` (`docs: record vets backfill dry-run results`).

## 1. Baseline verification

Confirmed locally before writing this checklist:

- Backend: **1138 passed, 17 skipped, 0 failed** (`dotnet test
  tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --nologo`).
- Frontend: **95 files / 832 tests passed** (`npx vitest run` in `client/`, run alone).
- `npx tsc -b`, `npm run lint`, and `npx vite build` passed.

## 2. Migrations this release expects to apply

In EF Core migration order (oldest first):

| Order | Migration ID | Change |
|------:|--------------|--------|
| 1 | `20261017165739_AddUserIsDemo` | Adds `Users.IsDemo` (`boolean NOT NULL DEFAULT false`). |
| 2 | `20261020152830_AddInventoryCategories` | Adds nullable `InventoryItems.CategoryId`, creates `InventoryCategories`, adds the indexes and the `SetNull` foreign key. |
| 3 | `20261022092559_AddVets` | Adds nullable `VetId` to `MedicalRecords`, `VaccinationRecords`, `BreedingRecords`, and `BirthRecords`; creates `Vets`, its indexes, and the four `SetNull` foreign keys. |

All three are additive: none drops or renames an existing column or table.
`AddInventoryCategories` and `AddVets` add nullable link columns; `AddUserIsDemo` adds a
non-null boolean with a database default of `false`. The two new tables and all new indexes
and foreign keys are additive.

> Production's `__EFMigrationsHistory` was **not** queried. Before deploying, the operator
> must confirm production already contains every earlier migration and does **not** contain
> these three IDs. If production already has any of them, stop and reconcile before deploy.

## 3. Startup migration behavior

`src/FMS.API/Program.cs` runs migrations automatically during normal API startup:

```csharp
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
    await dbContext.Database.MigrateAsync();
    await RoleSeeder.SeedRolesAsync(dbContext);
}
```

When background jobs are enabled, Hangfire's schema setup runs after that migration block.
The management commands
(`vets:backfill`, `inventory:categories:backfill`) are handled **before** host startup, so
they do not migrate or seed; a normal API start does.

### Are the migrations idempotent to re-run?

- **At the EF/application level: yes.** Each applied migration is recorded in
  `__EFMigrationsHistory`. `MigrateAsync()` applies only missing IDs, so restarting the API
  after a successful run is a no-op for these migrations.
- **At the raw SQL level: no.** The `Up` methods use ordinary `ALTER TABLE`, `CREATE TABLE`,
  `CREATE INDEX`, and `CREATE UNIQUE INDEX` statements, not `IF NOT EXISTS`. Do not replay
  migration SQL manually with psql; use EF Core.
- **Concurrent startup is not protected by this code.** The project uses EF Core 8, which has
  no cross-process migration lock. If several API instances start at once, one instance must
  apply the migrations first (or use a one-off migration/release step), then start the rest.

## 4. Pre-deployment gates

Complete every item and record the result before deploying.

- [ ] Release commit and image/client build SHA recorded.
- [ ] **Backup or point-in-time restore confirmed.** The database owner confirms a
      restorable backup or PITR window exists, its timestamp and retention, and that a
      restore has been tested. If available, take an on-demand snapshot immediately before
      deploy and record its ID.
- [ ] Production migration history checked read-only and the three IDs above are absent.
- [ ] Deploy window chosen for low traffic. The migration adds columns and constraints and
      creates indexes; those operations take locks on the affected tables.
- [ ] Connection string, JWT secret, email settings, and other production configuration
      confirmed present through the deployment platform, not in source control.
- [ ] API scaled to **one migrating instance**, or a separate migration job is ready, so
      multiple replicas do not race `MigrateAsync()`.
- [ ] Rollback owner and decision threshold agreed.

## 5. Deploy order

1. **Freeze or minimise writes** for the migration window.
2. **Deploy the API first.** On first start it applies, in order:
   `AddUserIsDemo` → `AddInventoryCategories` → `AddVets`, then seeds roles.
3. **Watch the startup logs.** Confirm each migration ID is applied and the API reaches a
   healthy state. A migration failure leaves the new API unable to start; stop and roll back.
4. **Deploy the client after the API is healthy.** New clients call the Vets and inventory
   category endpoints; older clients continue to use the retained string fields.
5. **Run the post-deploy smoke checks below.**

Do **not** run `vets:backfill --apply` or `inventory:categories:backfill --apply` as part of
this deployment. Both are separate, approved operator steps. The migrations alone leave
historical rows unlinked.

## 6. Post-deploy smoke checks

- [ ] **App loads.** Open the production URL in a clean browser profile; the login page and
      static assets load without console or network errors.
- [ ] **Login works.** Sign in with a non-demo account and reach the dashboard for the
      expected farm.
- [ ] **Health → Vets opens.** The Vets screen loads, lists existing vets (possibly empty),
      and the add/edit controls render without errors.
- [ ] **A medical record can select a vet.** Open Medical Records, create or edit a record,
      select an active vet from the picker, save, then reopen the record and confirm the
      selected vet name is shown.
- [ ] **Inventory categories list loads.** Open Inventory → Items and confirm the category
      selector/table loads existing categories; create or edit an item with a category and
      confirm it saves.
- [ ] **Existing data still reads.** Open an older medical/vaccination record and an older
      inventory item. Confirm their retained vet/category text is still visible.
- [ ] **API health and logs.** `/health` is ready and logs contain no migration, foreign-key,
      or startup errors.

If any check fails, stop and use the rollback plan.

## 7. Older-client compatibility confirmation

Both transitional strings remain populated by the new server paths.

### `VetName`

- Selecting a vet sends `vetId`; the services resolve the vet within the farm and write
  **both** `VetId` and the vet's name into `VetName`
  (`MedicalRecordService`, `VaccineService`, `BreedingService`).
- A free-text or legacy request with only `vetName` still writes `VetName` and leaves
  `VetId` null.
- Reads prefer the joined vet name and fall back to the retained `VetName` for unlinked rows.
- Covered by `VetClinicalSyncTests` for all four clinical record types.

### Inventory category string

- Creating an item with `categoryId` writes the resolved category name into the retained
  `Category` string; creating with only `category` writes the cleaned string and leaves
  `CategoryId` null (`InventoryItemRules.Build`, `InventoryService.CreateItemAsync`).
- Updating an item does the same (`InventoryService.UpdateItemAsync`).
- Reads prefer the joined category name and fall back to `Category`.
- Covered by `InventoryCategoryTests` and the inventory-categories E2E assertions.

**Mixed-version caveat:** an old API instance does not know the new ID columns. If it edits a
record that a new instance already linked, it updates the string but leaves the old ID in
place; the new API then reads the linked name. Keep the cutover short and avoid running old
and new writers against linked records for longer than necessary.

## 8. Rollback plan

### Preferred: roll back the application, keep the additive schema

1. Redeploy the previous API and client versions.
2. Leave `Vets`, `InventoryCategories`, `Users.IsDemo`, `VetId`, and `CategoryId` in place.
3. The old API ignores the new tables and columns. `VetId` and `CategoryId` are nullable;
   `Users.IsDemo` is non-null but has a database default of `false`, so old inserts still
   succeed.
4. Re-run the smoke checks against the old version.

This is the lowest-risk rollback. Do not run `Down` migrations merely because the application
was rolled back.

### Full schema rollback, only if required

Take a backup first and get explicit approval. Apply the `Down` methods in reverse order:

1. `20261022092559_AddVets`
2. `20261020152830_AddInventoryCategories`
3. `20261017165739_AddUserIsDemo`

This is destructive:

- `AddVets` down drops `Vets` and all four `VetId` columns. Vet rows and links created after
  deployment are lost.
- `AddInventoryCategories` down drops `InventoryCategories` and `InventoryItems.CategoryId`.
  Category rows and links created after deployment are lost.
- `AddUserIsDemo` down drops `Users.IsDemo`; the demo flag is lost.
- If a category or vet backfill was applied after deployment, a schema rollback also removes
  its links. Prefer point-in-time restore if that data matters.

## 9. Risks and open items

1. **Production history is unverified.** This checklist deliberately made no production
   connection. The operator must confirm the pending set before deploy.
2. **Concurrent auto-migration.** EF Core 8 has no cross-process migration lock. Multiple
   replicas starting together can race; migrate with one instance or a dedicated job.
3. **Locking on large tables.** Adding columns, foreign keys, and indexes can briefly block
   writes. Use a low-traffic window and monitor locks.
4. **Mixed-version writers.** Old API instances cannot update the new ID columns; a string
   edit can temporarily disagree with an existing link. Minimise the mixed-version window.
5. **Backfills are not part of deploy.** Historical `VetName` and `Category` rows stay
   unlinked until the separate backfill commands are approved and applied. The local dry run
   used a copy with zero clinical rows, so real-data grouping and duplicate handling remain
   unproven.
6. **`AddUserIsDemo` is not nullable.** Its default makes old inserts safe, but the general
   "new columns are nullable" rollback argument applies specifically to `VetId` and
   `CategoryId`, not to `IsDemo`.
7. **Frontend suite flakiness under load.** A repeat full `npx vitest run` with 95 isolated
   workers hit the 5s per-test timeout in `src/i18n/enumRender.test.tsx` and
   `src/pages/breeding/LineagePage.test.tsx`. Both files passed in isolation (21 tests, 27s).
   This looks like runner load, not a product regression; qualify the suite with a lower
   worker count or an isolated rerun if a full run times out.

## 10. Sign-off

| Item | Owner | Result / timestamp |
|------|-------|--------------------|
| Backup or PITR confirmed | | |
| Pending migration set confirmed | | |
| API deployed and migrated | | |
| Client deployed | | |
| Smoke checks passed | | |
| Rollback decision (if needed) | | |
