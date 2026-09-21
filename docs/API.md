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

One pipeline serves three entities — animals, **employees** and **inventory items** — over the same two endpoints per entity. Only the route prefix and the field vocabulary differ; the mapping, validation, duplicate and commit behaviour below is identical for all three, because the differences live in the importer's own row rules rather than in the wire shape.

| Entity | Route prefix | Authorized exactly like |
|---|---|---|
| Animals | `/api/farm/{farmId}/animals/import` | `POST /api/farm/{farmId}/animals` |
| Employees | `/api/farm/{farmId}/employees/import` | `POST /api/farm/{farmId}/employees` |
| Inventory items | `/api/farm/{farmId}/inventory-items/import` | `POST /api/farm/{farmId}/inventory-items` |

Importing therefore never becomes a way around a role that creating is subject to. Every route is farm-scoped like its single-record counterpart (X-Farm-Id header, membership enforced, route/header farm match). Both endpoints accept `multipart/form-data` with the file (`file`) and an optional JSON column mapping (`mapping`); CSV and `.xlsx` are parsed server-side, so any client imports through the same pipeline. `.xls` is rejected with a message asking for `.xlsx` or CSV.

- POST {prefix}/preview - Validate and report. Writes nothing.
- POST {prefix}/commit - Validate, then import. All-or-nothing.

Called with no mapping, `preview` auto-detects columns from the header row and returns `fields`, `headers`, `mapping`, `suggestedMapping` and the farm's `lookups` (so the UI can offer real values for a fixed-value mapping) along with `totalRows`, `validRowCount`, `invalidRowCount`, `invalidRows` and `sampleValidRows`. Passing a mapping overrides auto-detection for the fields it mentions; a field mapped to an empty object is ignored, and a field left out keeps the guess.

What each importer resolves against the farm:

- **Animals** — animal type, breed, sex, status and location by name; *sire tag* and *dam tag* resolve to an existing animal in the farm or to another row of the same file. The identifier is the tag number.
- **Employees** — the department and the employee role by name (a name matching nothing, or more than one record, is a row error naming what was in the file), and the salary type, which must be one of `Monthly`, `Weekly`, `Daily`, `Hourly`. The identifier is the email address; an employee with no email is unidentified and can never be a duplicate.
- **Inventory items** — nothing: category and location are free text on an item rather than shared records. The identifier is the item name, which the unique index on `(FarmId, Name)` also backs.

`mapping` shape: `{ "fields": { "tagNumber": { "column": 0 }, "status": { "constant": "Active" } }, "dateFormat": "dd/MM/yyyy" }`. Dates are read from ISO-8601 or a real Excel date cell; a slashed date is accepted only when it cannot be read two ways, otherwise the row is reported as ambiguous so the caller can set `dateFormat` instead of the import guessing.

Row validation reuses the create endpoint's own rules — the same rule function the single-record service runs (`AnimalRules`/`AnimalService`, `EmployeeRules`/`EmployeeService`, `InventoryItemRules`/`InventoryService`) — so a row cannot be accepted here that a hand-entered record would be rejected for, and the error text is the endpoint's own sentence rather than a parallel copy of it. An identifier already used in the farm is an error (never a silent skip and never an overwrite), as is one repeated inside the file.

Employee imports additionally enforce the field's length caps (a 30-character phone, 200-character email, 500-character address, 1000-character note) and email uniqueness in **both** paths: an import that may create what `POST /employees` refuses is not parity, it is a bypass.

`commit` re-reads and re-validates the uploaded file rather than trusting a preview, then writes the whole batch in one `SaveChanges` — one transaction — so `importedCount` is either `0` or `totalRows`. A file with any invalid row answers **200** with `importedCount: 0` plus the problem rows (the shape `POST /animals/bulk/status` already uses); file-level problems (missing file, oversized file, unreadable mapping, unsupported format, required field unmapped) answer **400**.

Limits (`Import` section): `MaxRows` (5000), `MaxFileBytes` (10 MB), `MaxReportedRows` (500 — how many problem rows a response carries, not how many exist), `SampleValidRows` (10). Before this subphase the same keys lived in an `AnimalImport` section; that spelling is still read for any key the `Import` section leaves unset, so a deployment that had raised `MaxRows` keeps its raised limit. `Import` wins whenever both are present, and the base `appsettings.json` deliberately defines neither — a default there would set the keys the fallback tests for.

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

## Error Responses
All errors follow RFC 7807 Problem Details format with traceId for debugging.
