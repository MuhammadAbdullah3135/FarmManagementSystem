# FMS API Documentation

## Authentication
All API requests require a JWT Bearer token in the Authorization header.

### Endpoints
- POST /api/auth/register - Create account
- POST /api/auth/login - Get access + refresh tokens
- POST /api/auth/refresh - Refresh expired access token
- POST /api/auth/reset-password - Request password reset
- GET /api/auth/me - Get current user info

## Farm Context
All farm-scoped endpoints require the X-Farm-Id header.

## Rate Limiting
- General API: 300 requests/minute per user
- Returns 429 with Retry-After header when exceeded

## Health Checks
- GET /health - Full health check with DB connectivity
- GET /health/ready - Readiness probe (DB check)
- GET /health/live - Liveness probe (always healthy)

## Scheduled Jobs
Recurring work runs in-process via Hangfire, storing its tables in the `hangfire` schema of the same PostgreSQL database (created on startup after EF migrations). Cron expressions are UTC.

- GET /api/admin/jobs - Job status: recurring jobs (schedule, next/last run, last state, last error), queue counters and recent failures. Account-scoped SystemOwner endpoint; it needs no X-Farm-Id and is not affected by the farm-context gate.
- Each recurring job fans out one execution per active farm, so farm work is isolated, retried and logged individually.
- /hangfire - Hangfire dashboard, mounted in Development only.

Configuration (`Jobs` section): `Enabled`, `MaxRetryAttempts`, per-job `Enabled`/`Cron`, and `HealthSnapshot:StalenessMinutes` (how old the dashboard's health snapshot may be before it falls back to a live calculation). `Jobs:Enabled=false` runs an instance with no scheduler.

## Notifications
Farm-scoped and always scoped to the calling user: the recipient is taken from the JWT, never from the request, so one member cannot read or acknowledge another's notifications. Requires the X-Farm-Id header like any other farm route, and needs no exemption from the farm-context gate.

- GET /api/farm/{farmId}/notifications - The caller's notifications. Query: `unreadOnly`, `includeDismissed`, `includeResolved`, `page`, `pageSize`. Returns `items`, `totalCount` and `unreadCount` (unread, undismissed and unresolved — what the header badge shows).
- GET /api/farm/{farmId}/notifications/unread-count - The unread count alone.
- POST /api/farm/{farmId}/notifications/{id}/read - Mark one read. Idempotent. A notification belonging to somebody else answers 404.
- POST /api/farm/{farmId}/notifications/read-all - Mark every unread notification read; returns how many changed.
- POST /api/farm/{farmId}/notifications/{id}/dismiss - Hide it from the default list. The row survives so history is not rewritten, and dismissing also marks it read.
- GET /api/farm/{farmId}/notifications/preferences - The full alert-type × channel matrix, with the server's defaults filled in for anything not overridden.
- PUT /api/farm/{farmId}/notifications/preferences - Upsert channel choices. An unknown alert type is rejected with 400 rather than ignored.

Notification content is produced by the `notification-dispatch` job from the same alert computation the dashboard uses (`/api/farm/{farmId}/dashboard/alerts`), so the two cannot disagree. Each recipient receives each condition once: the job keys on the identity of the condition, refreshes a row whose details changed rather than re-notifying, and resolves one whose condition cleared — which is what allows it to alert again if the condition returns.

Configuration (`Notifications` section): `EmailMinSeverityOnByDefault` (default `Critical` — the severity at or above which an alert type is emailed unless the recipient opts out) and `MaxAlertsPerEmail` (digest cap). Email is the only out-of-app channel; the transport itself is still the placeholder `EmailService`, so digests are logged rather than delivered until a provider is configured. Push and SMS are not implemented.

## Bulk import

One pipeline serves seven entities — animals, **employees**, **inventory items**, **suppliers**, **customers**, **expenses** and **income records** — over the same two endpoints per entity. Only the route prefix and the field vocabulary differ; the mapping, validation, duplicate and commit behaviour below is identical for all of them, because the differences live in the importer's own row rules rather than in the wire shape. (Expenses and income records are the one deliberate exception to the duplicate rule — see below.)

| Entity | Route prefix | Authorized exactly like |
|---|---|---|
| Animals | `/api/farm/{farmId}/animals/import` | `POST /api/farm/{farmId}/animals` |
| Employees | `/api/farm/{farmId}/employees/import` | `POST /api/farm/{farmId}/employees` |
| Inventory items | `/api/farm/{farmId}/inventory-items/import` | `POST /api/farm/{farmId}/inventory-items` |
| Suppliers | `/api/farm/{farmId}/inventory/suppliers/import` | `POST /api/farm/{farmId}/inventory/suppliers` |
| Customers | `/api/farm/{farmId}/inventory/customers/import` | `POST /api/farm/{farmId}/inventory/customers` |
| Expenses | `/api/farm/{farmId}/finance/expenses/import` | `POST /api/farm/{farmId}/finance/expenses` |
| Income records | `/api/farm/{farmId}/finance/income-records/import` | `POST /api/farm/{farmId}/finance/income-records` |

Importing therefore never becomes a way around a role that creating is subject to. Every route is farm-scoped like its single-record counterpart (X-Farm-Id header, membership enforced, route/header farm match). Both endpoints accept `multipart/form-data` with the file (`file`) and an optional JSON column mapping (`mapping`); CSV and `.xlsx` are parsed server-side, so any client imports through the same pipeline. `.xls` is rejected with a message asking for `.xlsx` or CSV.

- POST {prefix}/preview - Validate and report. Writes nothing.
- POST {prefix}/commit - Validate, then import. All-or-nothing.

Called with no mapping, `preview` auto-detects columns from the header row and returns `fields`, `headers`, `mapping`, `suggestedMapping` and the farm's `lookups` (so the UI can offer real values for a fixed-value mapping) along with `totalRows`, `validRowCount`, `invalidRowCount`, `invalidRows` and `sampleValidRows`. Passing a mapping overrides auto-detection for the fields it mentions; a field mapped to an empty object is ignored, and a field left out keeps the guess.

What each importer resolves against the farm:

- **Animals** — animal type, breed, sex, status and location by name; *sire tag* and *dam tag* resolve to an existing animal in the farm or to another row of the same file. The identifier is the tag number.
- **Employees** — the department and the employee role by name (a name matching nothing, or more than one record, is a row error naming what was in the file), and the salary type, which must be one of `Monthly`, `Weekly`, `Daily`, `Hourly`. The identifier is the email address; an employee with no email is unidentified and can never be a duplicate.
- **Inventory items** — nothing: category and location are free text on an item rather than shared records. The identifier is the item name, which the unique index on `(FarmId, Name)` also backs.
- **Suppliers** — nothing: contact information and the products supplied are free text. The identifier is the supplier name, which the unique index on `(FarmId, Name)` also backs.
- **Customers** — nothing to resolve; the identifier is the customer name, backed by the same unique index on `(FarmId, Name)`.
- **Expenses** — the expense category and the payment method by name (each must match exactly one record in the farm), plus an optional location by name and an optional *animal tag* that resolves to an animal in the farm for cost traceability.
- **Income records** — the same shape against the income vocabulary: income category and payment method by name, plus an optional location and animal tag.

`mapping` shape: `{ "fields": { "tagNumber": { "column": 0 }, "status": { "constant": "Active" } }, "dateFormat": "dd/MM/yyyy" }`. Dates are read from ISO-8601 or a real Excel date cell; a slashed date is accepted only when it cannot be read two ways, otherwise the row is reported as ambiguous so the caller can set `dateFormat` instead of the import guessing.

Row validation reuses the create endpoint's own rules — the same rule function the single-record service runs (`AnimalRules`/`AnimalService`, `EmployeeRules`/`EmployeeService`, `InventoryItemRules`/`InventoryService`, `SupplierRules`/`SupplierService`, `CustomerRules`/`CustomerService`, `ExpenseRules`/`FinanceService`, `IncomeRules`/`FinanceService`) — so a row cannot be accepted here that a hand-entered record would be rejected for, and the error text is the endpoint's own sentence rather than a parallel copy of it. An identifier already used in the farm is an error (never a silent skip and never an overwrite), as is one repeated inside the file.

Employee imports additionally enforce the field's length caps (a 30-character phone, 200-character email, 500-character address, 1000-character note) and email uniqueness in **both** paths: an import that may create what `POST /employees` refuses is not parity, it is a bypass.

**Duplicate policy.** Where an entity has an identifier, a duplicate is an error: an animal's tag number, an inventory item's name, an employee's email, and a supplier's or customer's name. A financial record has no such identifier — there is no code column, and two expenses (or two income records) of the same amount on the same day are legitimately distinct transactions — and `POST /finance/expenses` and `POST /finance/income-records` enforce no duplicate. The expense and income importers therefore perform **no duplicate detection**: inventing a heuristic key (date + amount + category) would reject legitimate rows and make a file stricter than the add form. This is a disclosed limitation rather than an oversight, and the wizard shows it on the review step before commit.

`commit` re-reads and re-validates the uploaded file rather than trusting a preview, then writes the whole batch in one `SaveChanges` — one transaction — so `importedCount` is either `0` or `totalRows`. A file with any invalid row answers **200** with `importedCount: 0` plus the problem rows (the shape `POST /animals/bulk/status` already uses); file-level problems (missing file, oversized file, unreadable mapping, unsupported format, required field unmapped) answer **400**.

Limits (`Import` section): `MaxRows` (5000), `MaxFileBytes` (10 MB), `MaxReportedRows` (500 — how many problem rows a response carries, not how many exist), `SampleValidRows` (10). Before this subphase the same keys lived in an `AnimalImport` section; that spelling is still read for any key the `Import` section leaves unset, so a deployment that had raised `MaxRows` keeps its raised limit. `Import` wins whenever both are present, and the base `appsettings.json` deliberately defines neither — a default there would set the keys the fallback tests for.

## Full-farm export

`POST /api/farm/{farmId}/export` — queue an export of the farm's whole dataset.
`GET /api/farm/{farmId}/export` — the export's state plus the archive's manifest (200 with a null body when the farm has never been exported).
`GET /api/farm/{farmId}/export/download` — the archive: a redirect to a short-lived presigned URL on object storage, streamed from this endpoint on local disk. Never publicly addressable, and the path always comes from the requesting farm's own record.

Farm-scoped like every other `/api/farm/{farmId}/…` route (X-Farm-Id header, membership enforced, route/header farm match), and authorized exactly as farm administration is: `SystemOwner` and `FarmManager`. The archive is every record the farm holds at once — finance, payroll, health, inventory — so it sits behind the same farm-admin boundary as member management, and it is the single most sensitive read in the application.

**Format: one ZIP of per-entity CSVs**, UTF-8 with a BOM and `\n` line endings — the same byte shape the app's own exports produce and its own import reader accepts. Not a multi-sheet workbook: CSV is what the importers already read, so the seven re-importable files are a format the app consumes rather than a second representation to keep in step, and a ZIP deflates as it goes, so a farm with years of history costs a streaming read rather than a workbook object graph held in memory.

**Built by the background job subsystem, not inside the request.** The request records the export, hands it to the job queue and answers — one round trip regardless of how much history the farm holds, which is what makes a timeout structurally impossible rather than merely unlikely. The job assembles the archive, stores it, records the outcome and writes an in-app `ExportReady` notification to whoever asked. With `Jobs:Enabled = false` nothing would ever run it, so the request refuses with **503** and a message naming the setting rather than accepting work nobody will do.

**What is in it.** The seven entities that have an importer come first, exported in that importer's own column vocabulary — the labels, in the importer's own order — so each file re-imports with no column mapping at all. Every other farm-scoped record table follows as its own CSV: the lookups that give those records their meaning (animal types, breeds, statuses, locations, categories, payment methods, …) and the transactional tables no importer covers (weight records, feed records, medical records, tasks, attendance, payroll, breeding, audit log, notifications, membership, …). Ids are kept alongside the resolved names, so a file is both readable and unambiguous. Soft-deleted rows are **included**, with their `isDeleted`/`deletedAt`/`deletedBy` values: an archive that quietly drops history is not a full archive.

Deliberately excluded, each with its reason in the manifest: derived data (the health-status snapshot, and every report endpoint including cost per animal — reproducible from these files, while the reverse is not true), credentials (`RefreshToken`, `PasswordResetToken`), the offline-sync idempotency ledger (`ProcessedMutation`), the account and its members' sign-in records, and the **bytes** of uploaded files — animal images and documents appear as their metadata rows with their storage paths, but the files themselves are not in the archive.

`manifest.json` carries the farm id and name, the generation time, the format, every file's row count and column list, the exclusions with their reasons, and the disclosed limitations. `README.txt` is the same information in prose. Both travel inside the ZIP; the manifest is also stored on the record so the status endpoint can report it without opening the archive.

**Lifecycle.** One record per farm (a unique index): `Queued` → `Running` → `Completed`/`Failed`. A second request while a build is in flight answers with the same export instead of queueing another; a request after a build re-queues it, and the previously built archive stays downloadable meanwhile — a failed rebuild never takes away the copy the farm already had. On completion an in-app notification links to `/dashboard/configuration/export`, where the archive can be downloaded. That notification type is deliberately **not** in `NotificationAlertTypes.All`, which is the dashboard's alert vocabulary and the dispatcher's auto-resolve set: adding it there would have the scheduled dispatch resolve it as a cleared condition minutes after it arrived.

Limits (`Export` section): `MaxArchiveBytes` (512 MB — an export that silently produced a truncated file would look complete to whoever downloaded it, so it is refused instead), `IncludeFarmNameInFileName` (true). Re-exporting replaces the previous archive, so a farm's storage cost is bounded to one file without a retention job.

Re-importing the money files deserves the warning it already carries on the import side: expenses and income records have no identifier, so these importers perform no duplicate detection. Importing `expenses.csv` or `income-records.csv` into a farm that already holds those records will create them a second time.

## Cost per animal report

`GET /api/farm/{farmId}/reports/cost-per-animal?from&to` — what each animal (and each herd) cost and earned in a date range, with every shared cost attributed by a stated basis. Farm-scoped like every other `/api/farm/{farmId}/…` route (X-Farm-Id header, membership enforced, route/header farm match), and authorized exactly as the other report routes are. Both dates are inclusive; leaving both out uses the feed report's default window (the last 30 days), so two reports cannot disagree about what "no dates" means.

One response carries the rows, the rollups and the arithmetic behind them:

- `animals[]` — per animal: `presentFrom`/`presentTo`, `animalDays`, `shareOfFarmDays`, `costs[]`, `revenue[]`, `totalCost`, `totalRevenue`, `margin`, and per-animal `warnings`.
- `herds[]` — the same animals grouped by where they are now, and by construction the sum of their rows.
- `farm` — totals and `costPerAnimalDay`.
- `rules[]` — the allocation rules in words, so the UI explains itself from one source instead of restating them.
- `warnings[]` — see below.
- `reconciliation` — the arithmetic, stated so it can be checked rather than trusted.

Every component is either `direct` (a record that names the animal, copied unchanged) or a stated fraction of a pool: `location-animal-days` (a pen's group feed, or an expense or income recorded against a location) or `farm-animal-days` (salary payments, and expenses or income with neither an animal nor a location). An allocated component carries `poolAmount`, `allocatedDays` and `poolDays` — the three numbers the UI shows as `days / pool-days × pool` — plus any `roundingAdjustment`. Shares are rounded to cents and the remainder lands on the largest holder, so a pool's parts add up to the pool exactly instead of a cent short.

A pen's pool is spread over the animals that were *in that pen*, reconstructed from the transfer history, so an animal that moved mid-range carries the right share of each pen. An animal's days come from its acquisition date (or date of birth) to the day it left — read from the status-change timeline event, which now records the status moved to in `RelatedEntityId`, with the event's own title as the fallback for rows written before that. Money that reached no animal is reported as unallocated with a reason, never absorbed into the rows.

Health money is counted exactly once: a veterinary expense that a medical or vaccination record created is counted from the health record (`reconciliation.healthLinkedExpenses`), not from the expense row as well.

The report deliberately does not price three things, and names each one instead of estimating: medicine issued and inventory drawn down (neither records a cost anywhere) and feed *purchases* (feed is costed when consumed, so adding the purchase would double-count). `reconciliation.costOutsideTheExpenseLedger` is the feed and labour cost this report carries that the profit-and-loss expense line does not — which is why its total is legitimately larger than the P&L's.

Warning codes, all in `warnings[]`: `feed.not-recorded`, `labour.not-recorded` (a zero that means "nothing was recorded", not "it was free"), `pool.unallocated` (with `amount`), `animal.presence-start-unknown` and `animal.departure-unknown` (with `affectedCount` — an animal counted as present for the whole range, so its share is overstated), `excluded.medicine-cost`, `excluded.inventory-consumption`, `excluded.feed-purchases` (with `affectedCount`), and `rows.unassigned`, which should never appear: it means money in a source total reached no row, and it is stated rather than hidden.

## Offline mutations (sync)

`POST /api/farm/{farmId}/sync/mutations` applies work a device did while it was offline. Farm-scoped exactly like every other farm route (X-Farm-Id header, membership enforced, route/header farm match), authorized exactly as the three workflows it applies — animal weights, attendance and task completion all require any farm member today, and the endpoint's per-operation role requirement is asserted against those controllers' own attributes by a test, so a workflow that gains a role cannot be reached through sync without it.

Body: `{ "items": [ { "operation", "clientMutationId", "payload" } ] }`, up to 200 items (a larger request is a 400 — a transport guard, not the queue-cap policy). Operations and their payloads:

| Operation | Payload | Applied by |
|---|---|---|
| `weight.record` | `animalId`, `weightKg`, `recordedAt?`, `notes?` | `IAnimalService.AddWeightAsync` |
| `attendance.checkIn` | `employeeId`, `occurredAt?` | `IAttendanceService.CheckInAsync` |
| `attendance.checkOut` | `employeeId`, `occurredAt?` | `IAttendanceService.CheckOutAsync` |
| `task.complete` | `taskId`, `completionNotes?`, `occurredAt?` | `IFarmTaskService.CompleteTaskAsync` |

Each item is applied by calling the workflow's own service method with the route's farm id — not a parallel implementation — so the sync path cannot accept what the live endpoint refuses, cannot answer with different words for the same input, and refuses an item naming another farm's animal with the same "Animal not found" a forged live request gets. The device timestamp is validated against the same 5-minute future tolerance the weight endpoint always had, now one shared constant (a queued 06:55 check-in is recorded at 06:55, not at the hour it synced).

The response is the import pipeline's result shape with a per-item outcome, and the batch always answers **200** when the request is well-formed:

- `requestedCount`, `successCount` (accepted + superseded), `acceptedCount`, `supersededCount`, `rejectedCount`
- `items[]` — `index`, `clientMutationId`, `outcome`, `message`, `targetEntityId`, `result` (the workflow's own DTO, exactly as the single-record endpoint returns it)
- `failures[]` — `index` + `message` for every rejected item, so a client can quarantine one row and keep the rest

Outcomes, as the enum names the API writes for every other enum: **`Accepted`** — the workflow ran and its effect is committed; **`Superseded`** — nothing was written because existing state already means what the item asked for, so the device may treat it as done; **`Rejected`** — nothing was written and the server's own message explains why.

Which of the last two a workflow produces is its conflict policy, not a judgement made from the message text. The services report an already-satisfied change with the distinct `Superseded` error code (both endpoints still answer **409**, as they always did), and the sync layer reads the code:

| Workflow | State it meets | Outcome |
|---|---|---|
| `weight.record` | — | Always `Accepted`: two measurements are two facts, never merged |
| `attendance.checkIn` | A day that already has an **earlier** check-in | `Superseded` — earliest wins; the stored time stands |
| `attendance.checkIn` | A day with a **later** stored check-in | `Accepted` — the stored check-in moves back to the device's time |
| `attendance.checkIn` | A day entered by hand (no check-in time, e.g. Leave) | `Superseded`, naming the status that stands: a device's clock does not overturn what a person typed |
| `attendance.checkOut` | A **later** stored check-out | `Superseded` — latest wins; the later time stands |
| `attendance.checkOut` | An **earlier** stored check-out | `Accepted` — the stored check-out moves forward, and `HoursWorked` follows |
| `attendance.checkOut` | No record for that day at all | `Rejected` ("No attendance record found for today") — a check-out with no shift to close |
| `task.complete` | Already completed with **equivalent** notes (absent ≡ blank) | `Superseded` — the intent is satisfied and nothing is written twice |
| `task.complete` | Already completed by this same `clientMutationId` | `Superseded` ("This completion was already applied") — the crash window, where the ledger row does not exist yet |
| `task.complete` | Already completed with **different** notes | `Rejected` — the record and the device disagree about a fact, so a human decides; the recorded notes are left untouched |
| `task.complete` | Cancelled | `Rejected` ("Open tasks only can be completed. Current status: Cancelled") — the server owns the lifecycle |

Only a supplied device time can move a stored time: a live request's `occurredAt` is merely the server's clock at arrival, which is not evidence about when a shift started or ended, so a live check-in or check-out on a day that already has a record is the conflict it has always been. Attendance's identity is the `(EmployeeId, Date)` unique index — one row per employee-day across every device — while its content is the device's, which is what makes the two rules above possible at all.

Idempotency: `clientMutationId` (a device-generated GUID, required) makes a retry safe. The server keeps a `ProcessedMutation` row per applied mutation and answers a repeat with **the result recorded the first time**, byte for byte, instead of re-running the workflow or creating a second record; the id is also stamped on the row the workflow created, behind a unique `(FarmId, ClientMutationId)` index, so even a retry that races past the ledger (or one whose ledger row was lost with the process) is refused by the database and reported as already applied rather than duplicated. Items whose outcome is `Rejected` are not recorded — they wrote nothing, so a retry after the blocking state changes (a manager reopens a task) can still succeed — and neither are `Superseded` ones.

Live single-record endpoints accept no idempotency key, so idempotency is unreachable from them and their status codes are unchanged (`Superseded` answers 409, as `Conflict` always did). Two live **messages** changed when this policy landed: a check-in against a day somebody entered by hand now says which status stands, and completing a task that is already complete now says whether it is already done or whether the notes disagree, instead of "Open tasks only can be completed. Current status: Completed" for both. A live check-in or check-out with no body behaves exactly as before.

## Incremental reads (`updatedSince`)

The three collections a device caches can be read as deltas. Each accepts `?updatedSince=<ISO instant>` — the `cursor` from the previous read, never the device's own clock — and answers with the same envelope whether it was asked for a delta or the whole collection:

| Route | Cacheable collection |
|---|---|
| `GET /api/farm/{farmId}/tasks?updatedSince=` | tasks (`pageSize` 10, first page, no filters) |
| `GET /api/farm/{farmId}/employees?updatedSince=` | employees (the roster) |
| `GET /api/farm/{farmId}/weight-schedules/status?updatedSince=` | the weight-check projection (computed) |

```json
{
  "items": [ /* the workflow's own DTOs — unchanged shapes */ ],
  "page": 1, "pageSize": 3, "totalCount": 3, "totalPages": 1,
  "deletedIds": ["6f1c…"],
  "cursor": "2026-09-23T08:00:00.0000000Z",
  "requiresFullSync": false
}
```

- **`items`** — only the rows whose `COALESCE(ModifiedAt, CreatedAt)` is after `updatedSince`, plus the ids of rows deleted since. A row that was created and never edited is still reported: matching `ModifiedAt` alone would leave it invisible to every delta forever.
- **`deletedIds`** — rows that existed when the caller last synced and do not now. This is the field that cannot be inferred: a delta that omits a row is indistinguishable from one that has nothing to say about it, so removals are stated. Hard deletes are named from the audit log (which records `(FarmId, EntityType, EntityId, Timestamp)` on every delete, indexed on `(FarmId, Timestamp)`); a soft-deleted employee names itself, since its row is its own tombstone and it is left out of `items`.
- **`cursor`** — the server's timestamp for this read, sent back as `updatedSince` next time. It is returned on a full read too, which is what lets the *next* read be a delta.
- **`requiresFullSync`** — the cursor could not be answered precisely: it is older than 30 days, it is in the future, or the change cannot be narrowed to rows (an edited or removed weight-check schedule can take entries away, and a delta has no id for an entry that no longer exists). The client should then read the collection without `updatedSince` and replace its copy. Answering "nothing changed" to a cursor the server cannot interpret is the one reply that loses data silently, so it is never given.

A delta is bounded rather than paged: past 500 changed rows the answer is `requiresFullSync`, because paging a delta would require the client to walk every page before advancing its cursor and a row changed between two pages would be lost. Filters and paging are not applied to a delta — it answers "what changed in the collection" — and only the shapes listed above are delta-capable; every other list endpoint still returns `PagedResult` and never grows fields it does not fill.

**One response shape changed.** `GET …/weight-schedules/status` returned a bare array before this and now returns the envelope above: a delta needs a server-owned cursor to send back, and a bare array has nowhere to put one. `GET …/weight-schedules/status/overdue` is unchanged (it is the notification job's read, not a cached collection).

**And nothing about the rows changed.** For `weight-schedules/status` — the only one of the three whose wire shape moved — `items` is asserted to be equal, field for field, to the array the pre-`updatedSince` read still returns (`WeightCheckStatus_NoCursor_ReturnsExactlyTheRowsTheListReadReturns`, which also pins the row's field set, so a rename or a removal fails there rather than on a device whose cached rows stop lining up after a deploy). For `tasks` and `employees` the parameter is purely additive: with no `updatedSince` the query and the rows are what they always were, and a client that never sends one sees no change at all.

The guarantee this rests on is server-side and central: `FmsDbContext` stamps `CreatedAt` on insert (only when unset — an import or backfill that states its own time is never overwritten) and `ModifiedAt` on every modification, so a row cannot be written without a timestamp that a cursor can find. It is deliberately in the context rather than in each service: the write that matters most here is a soft delete, which is a modification that a service can perform without touching `ModifiedAt` at all.

## Error Responses
All errors follow RFC 7807 Problem Details format with traceId for debugging.
