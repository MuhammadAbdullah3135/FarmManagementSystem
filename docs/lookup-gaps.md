# Lookup gaps after Phase C

This memo records the free-text fields that still have no backend-maintained list, how
each one is classified, and what would be needed to close it. The Phase C.6 status table
above section 1 is the current snapshot. Bucket 1 and the client-side Bucket 2 work are
implemented; the Bucket 3 master lists remain proposals and nothing in those sections has
been built.

Line references are to the current working tree at the start of Phase C.6.

## 1. Field-by-field report

| # | Field | Control today | DTO property | Existing list endpoint? | Bucket |
|---|-------|---------------|--------------|-------------------------|--------|
| 1 | Medical record — `vetName` | `Input` at `client/src/pages/health/MedicalRecordsPage.tsx:315-316` | `CreateMedicalRecordRequest.VetName`, `src/FMS.Application/Health/MedicalRecordDtos.cs:13` | None. The nearest list is employees (`src/FMS.API/Controllers/EmployeesController.cs:20`). | 3 — new master list |
| 2 | Medical record — `dosage` | `Input` at `client/src/pages/health/MedicalRecordsPage.tsx:308-309` | `CreateMedicalRecordRequest.Dosage`, `src/FMS.Application/Health/MedicalRecordDtos.cs:12` | None. | 1 — select from existing data |
| 3 | Medical record — `diagnosis` | `Input` at `client/src/pages/health/MedicalRecordsPage.tsx:290-291` | `CreateMedicalRecordRequest.Diagnosis`, `src/FMS.Application/Health/MedicalRecordDtos.cs:9` | None. | 1 — select from existing data |
| 4 | Vaccination record — `vetName` | `Input` at `client/src/pages/health/VaccinationRecordsPage.tsx:237-238` | `CreateVaccinationRecordRequest.VetName`, `src/FMS.Application/Health/VaccineDtos.cs:52` | None. | 3 — new master list |
| 5 | Vaccination record — `batchNumber` | `Input` at `client/src/pages/health/VaccinationRecordsPage.tsx:242-243` | `CreateVaccinationRecordRequest.BatchNumber`, `src/FMS.Application/Health/VaccineDtos.cs:53` | Only per-medicine stock batches (`src/FMS.API/Controllers/MedicinesController.cs:61`); no farm-wide vaccine-batch list. | 3 — new master list |
| 6 | Breeding record — `vetName` | `Input` at `client/src/pages/breeding/BreedingRecordsPage.tsx:296-297` | `CreateBreedingRecordRequest.VetName`, `src/FMS.Application/Breeding/BreedingRecordDtos.cs:12` | None. | 3 — new master list |
| 7 | Birth record — `vetName` | `Input` at `client/src/pages/breeding/BirthRecordingPage.tsx:290-291` | `CreateBirthRecordRequest.VetName`, `src/FMS.Application/Breeding/BirthRecordDtos.cs:12` | None. | 3 — new master list |
| 8 | Gestation health check — `performedBy` | `Input` at `client/src/pages/breeding/GestationPage.tsx:293-294` | `LogGestationHealthCheckRequest.PerformedBy`, `src/FMS.Application/Breeding/GestationDtos.cs:16` | Employees: `src/FMS.API/Controllers/EmployeesController.cs:20` returns the people who can perform a check. | 1 — select from existing data |
| 9 | Medicine — `unit` | `Input` at `client/src/pages/health/MedicinesPage.tsx:263-264` | `CreateMedicineRequest.Unit`, `src/FMS.Application/Health/MedicineDtos.cs:8` | `GET .../medicines` returns each medicine's `Unit` (`src/FMS.API/Controllers/MedicinesController.cs:22`). | 1 — select from existing data |
| 10 | Medicine stock batch — `batchNumber` | `Input` at `client/src/pages/health/MedicinesPage.tsx:310-311` | `AddStockBatchRequest.BatchNumber`, `src/FMS.Application/Health/MedicineDtos.cs:49` | Only the stock rows for one medicine (`src/FMS.API/Controllers/MedicinesController.cs:61`); no ready-to-use batch registry. | 3 — new master list |
| 11 | Vaccine type — `defaultDosage` | `Input` at `client/src/pages/health/VaccineTypesPage.tsx:173-174` | `CreateVaccineTypeRequest.DefaultDosage`, `src/FMS.Application/Health/VaccineDtos.cs:8` | `GET .../vaccines` returns existing default dosages (`src/FMS.API/Controllers/VaccinesController.cs:22`), but there is no dosage list. | 1 — select from existing data |
| 12 | Inventory item — `category` | `Input` at `client/src/pages/inventory/InventoryItemsPage.tsx:188` | `CreateInventoryItemRequest.Category`, `src/FMS.Application/Inventory/InventoryDtos.cs:24` | None. | 3 — new master list |
| 13 | Inventory item — `unit` | `LookupOrText` at `client/src/pages/inventory/InventoryItemsPage.tsx:191` | `CreateInventoryItemRequest.Unit`, `src/FMS.Application/Inventory/InventoryDtos.cs:25` | None; now a fixed list plus the retained free-text input. | 2 — fixed option set |
| 14 | Feed type — `unit` | `Select` over `FEED_UNITS` at `client/src/pages/feed/FeedTypesPage.tsx:269-270` | `CreateFeedTypeRequest.Unit` is the `FeedUnit` enum, `src/FMS.Application/Feed/FeedDtos.cs:21` | Already a fixed domain enum, `src/FMS.Domain/Enums/FeedUnit.cs:3-12`. | Fixed enum — not a gap |
| 15 | Inventory adjustment — `reason` | `ReasonSelect` preset plus the retained `Input.TextArea` at `client/src/pages/inventory/InventoryItemsPage.tsx:231-239` | `RecordStockMovementRequest.Reason`, `src/FMS.Application/Inventory/InventoryDtos.cs:38` | None; now a fixed preset list that writes into the existing free-text reason. | 2 — fixed option set |
| 16 | Feed stock movement — reason | **No reason field.** The form only has `notes` at `client/src/pages/feed/FeedTypesPage.tsx:308-309`; the request has `Notes` at `src/FMS.Application/Feed/FeedDtos.cs:61`. | `RecordStockMovementRequest.Notes` | None, because there is no field to populate. | Schema gap — not a lookup-gap target |
| 17 | Supplier — `productsSupplied` | `Input.TextArea` at `client/src/pages/inventory/SuppliersPage.tsx:29` | `CreateSupplierRequest.ProductsSupplied`, `src/FMS.Application/Inventory/SupplierDtos.cs:20` | No product list. Inventory item names are the closest existing data (`src/FMS.API/Controllers/InventoryItemsController.cs:20`). | 1 — select from existing data |
| 18 | Feeding schedule — `label` | `LookupOrText` at `client/src/pages/feed/FeedingSchedulesPage.tsx:173` | `CreateFeedingScheduleRequest.Label`, `src/FMS.Application/Feed/FeedDtos.cs:204` | None; now fixed presets plus a retained free-text input. | 2 — fixed option set |
| 19 | Salary payment — `reference` | `Input` with a Generate button at `client/src/pages/hr/SalaryPaymentsPage.tsx:298` | `RecordSalaryPaymentRequest.Reference`, `src/FMS.Application/Employees/EmployeeDtos.cs:124` | None. | 4 — auto-generate |

### Phase C.6 status

| # | Field | Status | Where it stands |
|---|-------|--------|-----------------|
| 1 | Medical `vetName` | Awaiting approval | Bucket 3 vet master only; not built. |
| 2 | Medical `dosage` | Done | `LookupOrText` backed by `GET /api/farms/{farmId}/suggestions/medicalDosage`. |
| 3 | Medical `diagnosis` | Done | `LookupOrText` backed by `GET /api/farms/{farmId}/suggestions/medicalDiagnosis`. |
| 4 | Vaccination `vetName` | Awaiting approval | Bucket 3 vet master only; not built. |
| 5 | Vaccination `batchNumber` | Awaiting approval | Bucket 3 batch master only; not built. |
| 6 | Breeding `vetName` | Awaiting approval | Bucket 3 vet master only; not built. |
| 7 | Birth `vetName` | Awaiting approval | Bucket 3 vet master only; not built. |
| 8 | Gestation `performedBy` | Done | Employee names loaded from the existing employees list. |
| 9 | Medicine `unit` | Done | Distinct units from the medicines already loaded on the page. |
| 10 | Medicine stock `batchNumber` | Awaiting approval | Bucket 3 batch master only; not built. |
| 11 | Vaccine type `defaultDosage` | Done | Existing dosages, plus `1 <linked medicine unit>` when a medicine is selected. |
| 12 | Inventory item `category` | Awaiting approval | Proposal written in `docs/inventory-categories-proposal.md`; no entity or migration created. |
| 13 | Inventory item `unit` | Done | Fixed list plus retained free text. |
| 14 | Feed type `unit` | Done | Domain enum; not a gap. |
| 15 | Inventory adjustment `reason` | Done | Preset reasons plus retained free text. |
| 16 | Feed stock movement reason | Still open | No reason field exists; needs a schema/DTO decision, not a lookup. |
| 17 | Supplier `productsSupplied` | Done | `LookupOrText` backed by `GET /api/farms/{farmId}/suggestions/supplierProductsSupplied`. |
| 18 | Feeding schedule `label` | Done | Fixed presets plus retained free text. |
| 19 | Salary payment `reference` | Done | Editable client-generated suggestion; duplicate employee/month references get `-2`, `-3`, … |

### What is not a gap

- **Feed type `unit` should stay fixed.** It is a domain enum, not a free text field. Its
  values are persisted as the enum and used for stock arithmetic and reports. Turning it
  into free text would break those calculations. Keep the existing select.
- **Feed stock movements have no `reason` field.** `RecordStockMovementRequest` only has
  `Notes`. Adding a reason select would require a DTO and database change, so it is out of
  scope for this phase.
- **Inventory movement history already displays `reason`** (`client/src/pages/inventory/InventoryMovementsPage.tsx:44`);
  the missing part is only the suggestion list on the adjustment form.

## 2. Classification

### Bucket 1 — Select from existing data

These values already exist in the database, but the page cannot list them cheaply today.
The safe pattern is a read-only suggestions endpoint (or a client-side derivation when the
source endpoint already returns a complete list). No schema change is needed. **Phase C.6
implemented this bucket.**

- Medical `dosage` and `diagnosis`: done through one read-only endpoint,
  `GET /api/farms/{farmId}/suggestions/{field}`, with the whitelist
  `medicalDiagnosis`, `medicalDosage`, `supplierProductsSupplied`. It returns at most 50
  distinct non-empty strings, most recently used first, scoped to the farm. Unknown fields
  return `404`. Medical fields follow the medical-record list's authenticated-member
  access; supplier products keep the supplier directory's
  `Employee`/`FarmManager`/`SystemOwner` role gate.
- Gestation `performedBy`: done. `LookupOrText` offers employee names from the existing
  employees list and keeps the current free-text escape hatch for a vet who is not an
  employee.
- Medicine `unit`: done. `LookupOrText` offers distinct units from the medicines already
  loaded on the page; the original free-text input remains.
- Vaccine type `defaultDosage`: done. Existing dosages are offered, plus
  `1 <linked medicine unit>` when a linked medicine is chosen. The field stays editable.
- Supplier `productsSupplied`: done through the same suggestions endpoint, using values
  previously stored on the farm's suppliers. The original text field remains beside the
  picker.

### Bucket 2 — Fixed option set

A short, stable list that belongs in code. These are implemented with the exact values
below.

- Inventory unit
- Inventory adjustment reason
- Feeding schedule label

### Bucket 3 — Needs a new master list

These need a farm-owned entity, a management screen, and a migration. They are proposals
only.

- **Vets** — medical, vaccination, breeding and birth `vetName`; optionally gestation
  `performedBy` if the farm wants vets and staff in one list.
- **Batch numbers** — vaccination `batchNumber`, medicine-stock `batchNumber`.
- **Inventory categories** — inventory item `category`.

### Bucket 4 — Auto-generate

- **Salary payment `reference`.** The app can build a readable reference from the payment
  period and the employee. This needs no schema change and is implemented in this phase.
- **Batch suggestions (future).** A rule such as `BATCH-YYYYMMDD-<sequence>` is possible,
  but there is no sequence source today. Do not implement it until a batch master or a
  server-side sequence exists.

## 3. Bucket 2 option lists (ready to paste)

### Inventory units

`client/src/utils/inventoryOptions.ts`

```ts
/**
 * Canonical inventory units. The field remains a string, so old values that are not in
 * this list still round-trip; the picker simply offers the common ones first.
 */
export const INVENTORY_UNITS = [
  'Piece',
  'Roll',
  'Box',
  'Bag',
  'Bottle',
  'Litre',
  'Millilitre',
  'Kilogram',
  'Gram',
  'Metre',
  'Pack',
  'Set',
] as const;
```

The retained free-text input is the escape hatch for a unit not listed here, so there is
no `Other` item that would store the literal word "Other".

### Inventory adjustment reasons

`client/src/utils/inventoryOptions.ts`

```ts
export const INVENTORY_ADJUSTMENT_REASONS = [
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Expired', label: 'Expired' },
  { value: 'Lost', label: 'Lost' },
  { value: 'Theft', label: 'Theft' },
  { value: 'Stock count correction', label: 'Stock count correction' },
  { value: 'Returned to supplier', label: 'Returned to supplier' },
  { value: 'Used for maintenance', label: 'Used for maintenance' },
] as const;
```

The preset `ReasonSelect` writes the chosen string into the existing free-text `Reason`
field, which stays on the form. A reason that is not in the list is typed into that field
directly; editing an old adjustment therefore never loses its text.

### Feeding schedule labels

`client/src/utils/feedOptions.ts`

```ts
export const FEEDING_LABELS = [
  'Morning feed',
  'Midday feed',
  'Evening feed',
  'Night feed',
  'Supplement',
] as const;
```

Custom labels remain available through the retained free-text input.

## 4. Bucket 3 entity sketches (proposal only)

These are not implemented. They are written down so the migration decision is explicit.

### 4.1 Vet

```text
Vet
  Id             Guid, PK
  FarmId         Guid, FK -> Farms, required, indexed
  Name           string(200), required
  Phone          string(50), optional
  Email          string(200), optional
  LicenseNumber  string(100), optional
  IsActive       bool, default true
  Notes          string(1000), optional
  CreatedAt      DateTime
  Unique: (FarmId, Name)
```

Owner: Health, managed from Health → Vets (or Configuration → Vets).

Migration proposal:

- Create `Vets`.
- Add nullable `VetId` to `MedicalRecords`, `VaccinationRecords`, `BreedingRecords`,
  `BirthRecords`, and `GestationHealthChecks` (for `performedBy`).
- Backfill by matching the existing `VetName` / `PerformedBy` strings, then leave the
  string columns in place until the UI stops writing them.

Alternative: treat `performedBy` as an employee reference and only create a `Vet` master
for the four clinical fields. That avoids duplicating staff who are already employees.

### 4.2 Batch number

```text
ProductBatch
  Id             Guid, PK
  FarmId         Guid, FK -> Farms, required, indexed
  ProductType    enum { Vaccine, Medicine }
  ProductId      Guid, required (VaccineType or Medicine, by ProductType)
  BatchNumber    string(100), required
  ExpiryDate     DateTime?, optional
  SupplierId     Guid?, FK -> Suppliers
  ReceivedDate   DateTime?, optional
  Notes          string(1000), optional
  Unique: (FarmId, ProductType, ProductId, BatchNumber)
```

Owner: Health/Inventory.

Migration proposal:

- Create `ProductBatches`.
- For medicines, the existing `MedicineStock` row already holds a batch number and expiry;
  decide whether to backfill from those rows or keep `ProductBatch` only for vaccines.
- Add nullable `BatchId` to `VaccinationRecords` and `MedicineStock`, keeping the string
  columns during the transition.

### 4.3 Inventory category

```text
InventoryCategory
  Id             Guid, PK
  FarmId         Guid, FK -> Farms, required, indexed
  Name           string(100), required
  Description    string(500), optional
  IsActive       bool, default true
  CreatedAt      DateTime
  Unique: (FarmId, Name)
```

Owner: Inventory, managed from Configuration → Inventory categories or the Inventory page.

**Full decision package:** `docs/inventory-categories-proposal.md`. It covers the field
list, owning module, migration and backfill, endpoints, UI change, estimated files, and the
open decisions. Status: **awaiting approval**; no entity or migration has been created.

Migration proposal:

- Create `InventoryCategories`.
- Add nullable `CategoryId` to `InventoryItems`.
- Backfill distinct non-empty `Category` strings, then keep the string column until the
  picker has been live for a release.

## 5. Bucket 4 rule

### Salary payment reference

Rule implemented on the client:

```text
SAL-<payment month YYYY-MM>-<employee key>
```

- The month comes from the payment date; if that is empty, from the period covered; if that
  is empty, from today.
- The employee key is the first initial plus the last name, uppercased and stripped to
  `A-Z0-9`, capped at 12 characters. If no employee is selected, the key is `EMP`.
- Example: Ayesha Khan paid in October 2026 → `SAL-2026-10-AKHAN`.
- The value is a suggestion in the existing free-text field. It is editable. When the same
  employee/month already uses the generated value (or `-2`, `-3`, …), the generator appends
  the next available numeric suffix. If the farm needs uniqueness across all time and
  employees, add an employee code (Bucket 3) or a server-side sequence.

### Batch suggestions (not implemented)

A future rule could be `BATCH-YYYYMMDD-<sequence>`, but the client cannot allocate the
sequence safely and the current schema has no batch registry. Do not build this until the
Bucket 3 batch decision is made.

## 6. Recommended build order

| Order | Work | Approximate files touched | Status |
|-------|------|---------------------------|--------|
| 1 | Bucket 2 selects: inventory unit, inventory adjustment reason, feeding label | 4 page/utility files + 3 test files + locale keys | Done |
| 2 | Bucket 4 salary reference | 1 page + 1 test + locale keys | Done |
| 3 | Bucket 1 client-side suggestions: gestation `performedBy`, medicine `unit`, vaccine `defaultDosage` | 3 pages + 3 tests | Done |
| 4 | Bucket 1 read-only suggestions: medical `diagnosis`/`dosage`, supplier `productsSupplied` | generic endpoint + 2 client pages + tests | Done |
| 5 | Bucket 3 inventory categories (smallest master) | proposal estimates 18–22 files + migration | Awaiting approval |
| 6 | Bucket 3 vets | 8–12 files + migration + backfill | Awaiting approval |
| 7 | Bucket 3 batches (or fold into medicine stock) | 6–10 files + migration + backfill | Awaiting approval |

The order is deliberate: no migration is needed for steps 1–4, and each master list is
smaller and less risky than the one after it.

## 7. Decisions needed before the next phase

1. **Answered during Phase C.6:** Bucket 1 uses one generic, whitelisted read-only
   endpoint rather than per-screen endpoints. The allowlist is closed and unknown fields
   return `404`.
2. **Answered:** inventory categories are the first Bucket 3 master. Approve the proposal
   in `docs/inventory-categories-proposal.md` before any entity or migration work.
3. **Open:** provide an employee code/number if references must be unique across
   employees; the same-employee/month collision fallback is implemented.
4. **Open:** approve the remaining Bucket 3 masters (vets and batches) after inventory
   categories. Neither has been started.
