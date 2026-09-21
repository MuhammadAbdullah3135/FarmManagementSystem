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

## Bulk animal import
Farm-scoped like the rest of the animal routes (X-Farm-Id header, membership enforced), and authorized exactly like a single `POST /api/farm/{farmId}/animals`. Both endpoints accept `multipart/form-data` with the file (`file`) and an optional JSON column mapping (`mapping`); CSV and `.xlsx` are parsed server-side, so any client imports through the same pipeline. `.xls` is rejected with a message asking for `.xlsx` or CSV.

- POST /api/farm/{farmId}/animals/import/preview - Validate and report. Writes nothing.
- POST /api/farm/{farmId}/animals/import/commit - Validate, then import. All-or-nothing.

Called with no mapping, `preview` auto-detects columns from the header row and returns `fields`, `headers`, `mapping`, `suggestedMapping` and the farm's `lookups` (so the UI can offer real values for a fixed-value mapping) along with `totalRows`, `validRowCount`, `invalidRowCount`, `invalidRows` and `sampleValidRows`. Passing a mapping overrides auto-detection for the fields it mentions; a field mapped to an empty object is ignored, and a field left out keeps the guess.

`mapping` shape: `{ "fields": { "tagNumber": { "column": 0 }, "status": { "constant": "Active" } }, "dateFormat": "dd/MM/yyyy" }`. Dates are read from ISO-8601 or a real Excel date cell; a slashed date is accepted only when it cannot be read two ways, otherwise the row is reported as ambiguous so the caller can set `dateFormat` instead of the import guessing.

Row validation reuses the create endpoint's own rules — the same `CreateAnimalRequest` FluentValidation rules and the same `AnimalService` creation checks — so a row cannot be accepted here that a hand-entered animal would be rejected for. A tag already used in the farm is an error (never a silent skip and never an overwrite), as is a tag repeated inside the file. *Sire tag* and *Dam tag* resolve to an existing animal in the farm or to another row of the same file.

`commit` re-reads and re-validates the uploaded file rather than trusting a preview, then writes the whole batch in one `SaveChanges` — one transaction — so `importedCount` is either `0` or `totalRows`. A file with any invalid row answers **200** with `importedCount: 0` plus the problem rows (the shape `POST /animals/bulk/status` already uses); file-level problems (missing file, oversized file, unreadable mapping, unsupported format, required field unmapped) answer **400**.

Limits (`AnimalImport` section): `MaxRows` (5000), `MaxFileBytes` (10 MB), `MaxReportedRows` (500 — how many problem rows a response carries, not how many exist), `SampleValidRows` (10).

## Error Responses
All errors follow RFC 7807 Problem Details format with traceId for debugging.
