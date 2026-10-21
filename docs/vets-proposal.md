# Vets — proposal

Status: **proposal awaiting approval**. No entity, migration, endpoint, backfill, or UI
change has been created. This document is the decision package for the second Bucket 3
master list, following `docs/inventory-categories-proposal.md`.

## Why vets next

`VetName` is free text on four clinical records:

- `MedicalRecord.VetName` (`src/FMS.Domain/Entities/MedicalRecord.cs`)
- `VaccinationRecord.VetName` (`src/FMS.Domain/Entities/VaccinationRecord.cs`)
- `BreedingRecord.VetName` (`src/FMS.Domain/Entities/BreedingRecord.cs`)
- `BirthRecord.VetName` (`src/FMS.Domain/Entities/BirthRecord.cs`)

The same person is typed separately on each screen, so spelling variants split history
and the health-cost report. A farm-owned vet list gives every screen one source of names
without changing what the clinical records store in this phase. Vet costs are already
grouped by the retained `VetName` string, so the list can land before any report change.

## Entity

Owner: **Health** module (`FMS.Domain` entity, `FMS.Infrastructure` service,
`FMS.API` controller, `client/src/pages/health`). Vets are clinical contacts, not
inventory-like configuration, and the Health module already owns medical and vaccination
records.

```text
Vet : AuditableEntity
  Id             Guid, PK
  FarmId         Guid, FK -> Farms, required, indexed
  Name           string(200), required
  Phone          string(50), optional
  Email          string(200), optional
  LicenseNumber  string(100), optional
  IsActive       bool, default true
  Notes          string(2000), optional
  CreatedAt      DateTime (from AuditableEntity)
  CreatedBy      Guid?      (from AuditableEntity)
  ModifiedAt     DateTime?  (from AuditableEntity)
  ModifiedBy     Guid?      (from AuditableEntity)
```

Why each field:

| Field | Reason |
|-------|--------|
| `Id` | Stable reference for the four nullable `VetId` columns and for future report grouping by identity. |
| `FarmId` | Every master list is farm-owned. It scopes reads, writes, uniqueness, and the backfill. |
| `Name` | The display name that replaces repeated free-text entry. Also the backfill key. |
| `Phone` | The usual way a farm reaches a vet; worth capturing once instead of in `Notes`. |
| `Email` | Optional second contact channel, matching the farm's other contact records. |
| `LicenseNumber` | Optional professional identifier. Unique per farm when present, so two spelling variants cannot claim the same licence. |
| `IsActive` | Retire a vet without deleting history or breaking records that still point at them. |
| `Notes` | Free context (clinic, availability, specialties) without adding speculative columns. |
| `CreatedAt` / audit columns | Consistent with `InventoryCategory` and the rest of the schema; lets operators see when a vet was added. |

Rules:

- `Name` is trimmed before validation and storage; empty names are rejected.
- `LicenseNumber` is trimmed; blank becomes `null` rather than an empty string.
- Names are unique **per farm, case-insensitively**, matching inventory categories:
  a PostgreSQL functional unique index on `("FarmId", lower("Name"))`, plus a service
  check so the API returns a validation conflict instead of a raw database error.
- `LicenseNumber` is unique **per farm, case-insensitively, only when present**: a partial
  functional unique index on `("FarmId", lower("LicenseNumber")) WHERE "LicenseNumber" IS
  NOT NULL`, plus the same service check.
- Deleting a vet that is referenced by any clinical record is blocked with a validation
  message; callers deactivate it instead. `IsActive=false` hides it from new selections but
  keeps it readable on existing records.
- A vet belongs to exactly one farm. Cross-farm sharing would need a join table and is out
  of scope (see open questions).

## Transitional columns on the four clinical records

```text
MedicalRecord      VetId Guid?, FK -> Vets, optional, indexed, OnDelete: SetNull
VaccinationRecord  VetId Guid?, FK -> Vets, optional, indexed, OnDelete: SetNull
BreedingRecord     VetId Guid?, FK -> Vets, optional, indexed, OnDelete: SetNull
BirthRecord        VetId Guid?, FK -> Vets, optional, indexed, OnDelete: SetNull
```

`VetName` stays on all four entities for at least one release. During the transition:

- Create/update requests accept `vetId` and retain `vetName`.
- When `vetId` is supplied, the API verifies the vet belongs to the same farm, is not
  missing, and writes the selected vet's name into `VetName` as well as its id into
  `VetId`. `VetName` remains optional for records with no vet.
- Reads return both `vetId` and `vetName`. Detail/list projections prefer the joined vet
  name and fall back to the retained string for rows that are not linked yet.
- Search continues to match `VetName`; no search behaviour changes in this phase.
- A later migration can drop `VetName` once every client writes `VetId`.

`GestationHealthCheck.PerformedBy` is deliberately **not** part of this phase. It already
uses the employee list as its closest existing data and is not a vet field.

### Write roles

Vet writes follow the **existing medical-record writers**: the medical-record create,
update, and delete actions are protected by `[Authorize]` plus farm membership resolution
in `FarmContextMiddleware`, which today means any member of that farm. The vet controller
must not be stricter or looser than that, so no user gains or loses access when this lands.
Reads also follow the existing clinical-record reads (any farm member). If the intent is
to narrow both surfaces to `Veterinarian`/`FarmManager`/`SystemOwner`, that should be a
separate, explicit decision for the medical-record endpoints too (see open questions).

## Migration steps

One migration, for example `AddVets`:

1. Create `Vets` with the columns above.
2. Add the two functional unique indexes (name; partial licence) and an
   `("FarmId", "IsActive")` index for the list query.
3. Add nullable `VetId` to `MedicalRecords`, `VaccinationRecords`, `BreedingRecords`, and
   `BirthRecords`, with the foreign key, index, and `OnDelete: SetNull`.
4. Do **not** backfill inside the migration. As with inventory categories, the backfill is
   a separate, dry-runnable operator step.
5. Leave every `VetName` populated. The down migration drops `VetId`, its indexes and
   foreign keys, and the `Vets` table; it does not touch `VetName`.

No production migration is run as part of this proposal. Approval is required before
creating the entity or migration.

## Backfill

A CLI command, following the inventory-category pattern:

```text
dotnet run --project src/FMS.API -- vets:backfill
dotnet run --project src/FMS.API -- vets:backfill --apply
```

Behaviour:

- Scope: all four clinical tables, per farm.
- Input: distinct, non-empty `trim(VetName)` values per farm. Blank, whitespace-only, and
  null names stay `null` and are reported as left null.
- Grouping: case-insensitive (`lower(trim(VetName))`), one row per farm and normalized
  name. The first spelling in deterministic order (case-insensitive, then ordinal) becomes
  the stored `Name`, matching the inventory-category rule.
- Existing `Vet` rows are matched by the same normalized key, so re-running is a no-op.
- Only records whose `VetId` is still `null` are linked; an existing link is never
  overwritten. `VetName` is never changed.
- Report counts: vets created, records linked per table, blank names left null, per farm.
- The dry run is read-only: the command is handled before API startup so it cannot trigger
  `MigrateAsync()` or role seeding, and the run happens in a transaction that is always
  rolled back (marked `READ ONLY` on PostgreSQL).
- `--apply` is the only writing path. It must not be run against production without
  explicit approval.

## Reporting

**Vet-cost grouping does not change in this phase.** `HealthCostService.GetCostsByVetAsync`
continues to group medical and vaccination costs by the retained `VetName` string exactly
as it does today. Consequences, accepted for one release:

- Backfilled and newly linked records report under the same names as before.
- Renaming a vet in the master list does not rewrite historical `VetName` values, so the
  cost report keeps the historical spelling until a later phase groups by `VetId`.
- No report, dashboard, export, or grouping code is modified by this phase.

## API routes

Base route follows the other farm-scoped controllers:
`api/farm/{farmId:guid}/vets`.

| Method | Route | Purpose | Access |
|--------|-------|---------|--------|
| `GET` | `/vets?includeInactive=false` | List the farm's vets, ordered by name | Same as clinical-record reads (any farm member) |
| `GET` | `/vets/{id:guid}` | Read one vet | Same as list |
| `POST` | `/vets` | Create `{ name, phone?, email?, licenseNumber?, notes? }` | Same as medical-record writes |
| `PUT` | `/vets/{id:guid}` | Rename, edit contact fields or notes, toggle `isActive` | Same as medical-record writes |
| `DELETE` | `/vets/{id:guid}` | Delete only when unreferenced; otherwise `409` with a "deactivate instead" message | Same as medical-record writes |

Every handler is farm-scoped in the query (`v.FarmId == farmId`); `FarmContextMiddleware`
continues to validate membership and role. The medical-record, vaccination-record,
breeding-record, and birth-record DTOs gain `vetId` alongside the existing `vetName`, and
the corresponding create/update services validate the link. Those DTO changes happen only
after approval.

## UI changes

1. **Management screen:** Health → Vets, a table with name, phone, email, licence, active
   state, and edit/deactivate actions. Add/edit uses a modal with Name (required), Phone,
   Email, Licence number, Notes, and Active.
2. **Medical records** (`client/src/pages/health/MedicalRecordsPage.tsx`): replace the
   `vetName` `Input` with a vet picker. Offer active vets plus the record's current inactive
   vet when editing, and keep a free-text fallback for a vet that is not in the list yet
   (the same `LookupOrText` pattern already used for inventory categories and diagnoses).
   Submitting a free-text name keeps `vetId` null.
3. **Vaccination records** (`client/src/pages/health/VaccinationRecordsPage.tsx`): same
   picker in place of the `vetName` input.
4. **Breeding records** (`client/src/pages/breeding/BreedingRecordsPage.tsx`): same picker.
5. **Birth recording** (`client/src/pages/breeding/BirthRecordingPage.tsx`): same picker.
6. **Vet-cost report** (`client/src/pages/health/VetCostsPage.tsx`): display only; no logic
   change. It keeps showing the grouped `VetName` strings returned today.
7. **Localisation:** add keys for the screen, validation, delete-blocked message,
   active/inactive labels, and picker placeholders in `en`, `es`, and `ar`. No English
   literals in components.
8. **No report or grouping change:** `HealthCostService` is untouched in this phase.

## Estimated files touched

Approximately **26–32 files**, including tests and the migration:

- `src/FMS.Domain/Entities/Vet.cs` (new)
- `src/FMS.Domain/Entities/MedicalRecord.cs`
- `src/FMS.Domain/Entities/VaccinationRecord.cs`
- `src/FMS.Domain/Entities/BreedingRecord.cs`
- `src/FMS.Domain/Entities/BirthRecord.cs`
- `src/FMS.Infrastructure/Persistence/FmsDbContext.cs`
- `src/FMS.Infrastructure/Persistence/Configurations/VetConfiguration.cs` (new)
- `src/FMS.Infrastructure/Persistence/Configurations/MedicalRecordConfiguration.cs`
- `src/FMS.Infrastructure/Persistence/Configurations/VaccinationRecordConfiguration.cs`
- `src/FMS.Infrastructure/Persistence/Configurations/BreedingRecordConfiguration.cs`
- `src/FMS.Infrastructure/Persistence/Configurations/BirthRecordConfiguration.cs`
- `src/FMS.Infrastructure/Persistence/Migrations/<timestamp>_AddVets.cs` (new)
- `src/FMS.Infrastructure/Persistence/Migrations/FmsDbContextModelSnapshot.cs`
- `src/FMS.Application/Health/VetDtos.cs` (new)
- `src/FMS.Application/Health/IVetService.cs` (new)
- `src/FMS.Application/Health/IVetBackfillService.cs` (new) and its report DTOs
- `src/FMS.Application/Health/MedicalRecordDtos.cs`
- `src/FMS.Application/Health/VaccineDtos.cs`
- `src/FMS.Application/Breeding/BreedingRecordDtos.cs`
- `src/FMS.Application/Breeding/BirthRecordDtos.cs`
- `src/FMS.Infrastructure/Health/VetService.cs` (new)
- `src/FMS.Infrastructure/Health/VetBackfillService.cs` (new)
- `src/FMS.Infrastructure/Health/MedicalRecordService.cs`
- `src/FMS.Infrastructure/Health/VaccineService.cs`
- `src/FMS.Infrastructure/Breeding/BreedingService.cs`
- `src/FMS.API/Controllers/VetsController.cs` (new)
- `src/FMS.API/VetBackfillCommand.cs` (new) and `src/FMS.API/Program.cs`
- `tests/FMS.Domain.Tests/Health/VetTests.cs` (new)
- `tests/FMS.Domain.Tests/Health/VetBackfillCommandTests.cs` (new)
- `tests/FMS.Domain.Tests/E2E/VetsE2ETests.cs` (new)
- `client/src/types/index.ts`, `client/src/api/health.ts`
- `client/src/pages/health/VetsPage.tsx` (new), `client/src/components/AppLayout.tsx`
- the four clinical pages listed above, `client/src/pages/health/VetCostsPage.tsx`
- locale resources (`en`/`es`/`ar`)

## Backfill dry run and database setup

The dry run needs a reachable PostgreSQL with the `AddVets` migration applied **before**
this proposal is implemented; until then there is nothing to dry-run. Once approved, the
operator command is handled before API startup, so it does not migrate or seed by itself.
The connection setup required is the same as inventory categories: a non-production
database whose connection string is supplied through `ConnectionStrings__DefaultConnection`
(or the configured default), with a role that can read the four clinical tables. I will ask
for explicit confirmation of that database before running the dry run, and will not run
`--apply` without a separate approval.

## Open questions

Defaults in bold are the ones this proposal assumes unless you answer otherwise.

1. **A vet belongs to one farm only.** No cross-farm sharing in this phase. A vet who
   works for several farms is entered once per farm; a shared-vet join table is a later
   decision.
2. **`LicenseNumber` is optional and unique per farm when present** (case-insensitive,
   trimmed, blank stored as `null`).
3. **Deleting a referenced vet is blocked; deactivate instead.** This mirrors inventory
   categories and keeps history readable.
4. **Vet writes keep the existing medical-record write rules** (today: any farm member,
   via farm context). Narrowing both to `Veterinarian`/`FarmManager`/`SystemOwner` is a
   separate decision because it changes existing clinical endpoints too.
5. **Renaming a vet does not rewrite historical `VetName` strings**, and vet-cost grouping
   stays on `VetName` for this phase. A later phase can switch grouping to `VetId`.
6. **`GestationHealthCheck.PerformedBy` stays employee-backed** and is not part of the vet
   list in this phase.
7. **No automatic creation from free text.** A typed name that does not match a vet stays
   `vetId = null` until someone adds it to the list; the backfill only runs as the
   explicit, dry-runnable operator step.
