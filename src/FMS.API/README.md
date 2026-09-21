# FMS API

ASP.NET Core 8 Web API backend for the Farm Management System. Provides all data operations, authentication, and business logic for the React frontend and mobile app.

## Tech

- **Framework:** ASP.NET Core 8 (minimal hosting, no Startup.cs)
- **ORM:** Entity Framework Core 8 with PostgreSQL (Npgsql)
- **Auth:** JWT (HMAC-SHA256) + BCrypt password hashing + refresh tokens
- **Validation:** FluentValidation (auto-scanned from assembly)
- **Logging:** Serilog (console + rolling file)
- **API docs:** Swagger/OpenAPI (Swashbuckle, dev only)
- **Rate limiting:** 300 requests/minute per user/IP
- **Caching:** `IMemoryCache` for configuration data (via `CachedConfigurationService` decorator)

## Running Locally

```bash
# Set connection string (or edit appsettings.json)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=fms;Username=postgres;Password=..."

# Run
dotnet run --project src/FMS.API
```

- API: `http://localhost:5200`
- Swagger UI: `http://localhost:5200/swagger` (opens automatically in dev)
- Health checks:
  - `GET /health` — full check (includes database connectivity)
  - `GET /health/ready` — readiness probe (database only)
  - `GET /health/live` — liveness probe (always healthy)

Database migrations and role seeding (SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer) run automatically on first startup.

### Docker

```bash
docker-compose up
```

Runs API on port 8080 with a SQL Server 2022 container (note: production uses PostgreSQL).

## Architecture

Clean Architecture with four projects:

```
FMS.API -> FMS.Infrastructure -> FMS.Application -> FMS.Domain
```

| Project | Purpose |
|---|---|
| `FMS.API` | Controllers, middleware, DI, Program.cs |
| `FMS.Application` | Service interfaces, DTOs, `Result<T>` pattern |
| `FMS.Domain` | Entities, enums, base classes (no dependencies) |
| `FMS.Infrastructure` | EF Core DbContext, service implementations, auth |

### Key Patterns

- **Result\<T\> monad:** All service methods return `Result<T>` or `Result` with error codes (`NotFound`, `Validation`, `Conflict`, `Unauthorized`, `Unexpected`). Controllers map these to HTTP status codes.
- **Farm-scoped multi-tenancy:** Every farm-scoped entity has a `FarmId` field. The `FarmContextMiddleware` validates access via the `X-Farm-Id` header.
- **Audit trail:** An EF Core `SaveChangesInterceptor` (`AuditLogInterceptor`) automatically captures all Create/Update/Delete operations with old/new values, user info, and IP address. Includes a recursion guard to prevent infinite loops when saving audit logs.
- **Soft delete:** Animals, Farms, Employees, MedicalRecords, and Expenses use an `IsDeleted` flag instead of hard deletes.
- **Repository-less design:** Services query `FmsDbContext` directly — no repository pattern.

## Middleware Pipeline

Requests are processed in this order:

1. `GlobalExceptionMiddleware` — RFC 7807 ProblemDetails error responses with trace IDs
2. Rate Limiter — 300 req/min per user/IP (fixed window)
3. Security Headers — X-Content-Type-Options, X-Frame-Options, CSP, XSS-Protection, Referrer-Policy, Permissions-Policy
4. Swagger (dev only)
5. HTTPS Redirection (production only)
6. CORS (configurable origins)
7. JWT Authentication (HMAC-SHA256, 15 min access / 30 day refresh, zero clock skew)
8. Authorization
9. `FarmContextMiddleware` — validates `X-Farm-Id` header against `UserFarms` table, sets farm context
10. File downloads — there is deliberately **no public static path** for uploads. Files are reachable only through authorized endpoints (see *File storage* below), which either stream the bytes or redirect to a short-lived presigned object-storage URL.

## File storage

Uploads (animal images and documents) go through `IFileStorageService`, which has two backends selected by `Storage:Provider`:

| Provider | Use | Downloads |
|---|---|---|
| `Local` (default) | development, tests, CI — no credentials needed | streamed by the API through an authorized endpoint |
| `S3` | production — any S3-compatible endpoint | short-lived presigned URL (default TTL 5 min) |

**Cloudflare R2 is the recommended object store**: it speaks the S3 API (so the same code serves AWS S3 or MinIO, and leaving `Storage__S3__ServiceUrl` empty targets real AWS S3) and charges no egress, which matters because farm photos are read far more often than written.

Two rules this design enforces:

- **Nothing is world-readable.** Authorization happens in the API *before* a URL is issued; the presigned URL is a bearer token, so its TTL is deliberately short.
- **Use `S3` in production.** The container filesystem is ephemeral — on Heroku every release and dyno restart discards locally stored files.

Configuration (no secrets in source; see `.env.example`):

```
Storage__Provider=S3
Storage__S3__Bucket=...
Storage__S3__Region=auto
Storage__S3__ServiceUrl=https://<account-id>.r2.cloudflarestorage.com
Storage__S3__AccessKeyId=...
Storage__S3__SecretAccessKey=...
Storage__PresignedUrlTtlMinutes=5
```

To move files that are still on local disk, run `scripts/migrate-uploads-to-s3.sh`.

## Key Endpoints

All farm-scoped endpoints require `Authorization: Bearer <token>` + `X-Farm-Id: <farmId>` headers.

### Auth (`/api/auth`)

| Method | Path | Description |
|---|---|---|
| POST | `/register` | Create account + user + default farm, then seed the full default lookup set |
| POST | `/login` | Returns JWT access + refresh tokens |
| POST | `/refresh` | Swap refresh token for new pair |
| POST | `/reset-password` | Email-based password reset |
| POST | `/revoke` | Revoke a refresh token |
| GET | `/me` | Current user info |

### Animals (`/api/farm/{farmId}/animals`)

| Method | Path | Description |
|---|---|---|
| GET | `/` | List with filters (search, type, breed, status, location) |
| POST | `/` | Create animal |
| GET | `/{id}` | Animal detail |
| PUT | `/{id}` | Update animal |
| DELETE | `/{id}` | Soft delete |
| PUT | `/{id}/status` | Change status with reason |
| POST | `/{id}/identifications` | Add identification |
| DELETE | `/{id}/identifications/{identificationId}` | Remove identification |
| GET | `/{id}/weights` | Weight records (paged) |
| POST | `/{id}/weights` | Add weight record |
| PUT | `/{id}/weights/{weightId}` | Update weight record |
| DELETE | `/{id}/weights/{weightId}` | Delete weight record |
| GET | `/{id}/weights/report` | Weight report (gain, ADG) |
| GET | `/{id}/timeline` | Event timeline (paged, filterable by type) |
| POST | `/{animalId}/images` | Upload image (5MB limit) |
| GET | `/{animalId}/images` | List images |
| DELETE | `/{animalId}/images/{imageId}` | Delete image |
| PUT | `/{animalId}/images/{imageId}/primary` | Set primary image |
| POST | `/{animalId}/documents` | Upload document (20MB limit) |
| GET | `/{animalId}/documents` | List documents |
| DELETE | `/{animalId}/documents/{documentId}` | Delete document |
| GET | `/{animalId}/documents/{documentId}/download` | Download document |
| POST | `/{animalId}/transfer` | Transfer animal between locations |
| POST | `/bulk/status` | Bulk status change |
| POST | `/bulk/transfer` | Bulk transfer |

### Breeding (`/api/farm/{farmId}/breeding-records`, `/gestation`, `/births`, `/lineage`)

| Method | Path | Description |
|---|---|---|
| CRUD | `/breeding-records` | Breeding records (sire, dam, method, result) — Vet/FarmManager/SystemOwner |
| GET | `/gestation` | List gestation records |
| GET | `/gestation/{id}` | Gestation detail |
| POST | `/gestation/confirm` | Confirm pregnancy |
| POST | `/gestation/{id}/revert` | Revert gestation |
| GET | `/gestation/{id}/health-checks` | Get health checks |
| POST | `/gestation/{id}/health-checks` | Log health check |
| GET | `/births` | List birth records |
| GET | `/births/{id}` | Birth detail |
| POST | `/births` | Create birth record — Vet/FarmManager/SystemOwner |
| DELETE | `/births/{id}` | Delete birth record |
| GET | `/lineage/{animalId}` | Ancestor/descendant tree (configurable depth) |
| GET | `/breeding/reports/summary` | Breeding summary report |
| GET | `/breeding/reports/trend` | Breeding trend report |
| GET | `/breeding/reports/methods` | Method distribution |
| GET | `/breeding/reports/sires` | Sire performance |
| GET | `/breeding/reports/calendar` | Upcoming calendar events |

### Feed (`/api/farm/{farmId}/feed/*`)

| Resource | Description |
|---|---|
| `/feed/types` | Feed type CRUD (Forage, Concentrate, Mineral, Supplement, Additive, Other) |
| `/feed/records` | Per-animal or per-location feed records (auto-deducts stock) |
| `/feed/inventory/movements` | Record stock movement (Purchase/Consumption/Adjustment) |
| `/feed/inventory/stock` | Current stock levels |
| `/feed/diet-plans` | Diet plans with itemized compositions |
| `/feed/diet-plans/{id}/items` | Add/remove diet plan items |
| `/feed/schedules` | Time-based feeding schedules |
| `/feed/tasks` | Generate, list, complete, or skip feeding tasks |
| `/feed/reports/*` | Consumption trends, by type/animal/location, cost summary |

### Health (`/api/farm/{farmId}/medical-records`, `/medicines`, `/vaccines`, `/vaccinations`, `/weight-schedules`, `/health/costs`)

| Resource | Description |
|---|---|
| `/medical-records` | Medical records with status tracking (Open/InProgress/Resolved) |
| `/medicines` | Medicine CRUD + stock batches + usage logging + alerts (low stock, expired, expiring) |
| `/vaccines` | Vaccine type management |
| `/vaccinations` | Vaccination records + schedules + overdue status + auto-linked expenses |
| `/vaccinations/status` | Vaccination status computation (upcoming/due/overdue) |
| `/vaccinations/status/overdue` | Overdue vaccinations only |
| `/weight-schedules` | Weight check schedules with status computation |
| `/weight-schedules/status/overdue` | Overdue weight checks |
| `/health/costs/*` | Cost reports: summary, by vet, by animal, by month |

### HR (`/api/farm/{farmId}/employees`, `/departments`, `/employee-roles`, `/attendance`, `/performance-reviews`)

| Resource | Description |
|---|---|
| `/employees` | Employee CRUD + salary payments + payroll report |
| `/departments` | Department management |
| `/employee-roles` | Role management |
| `/attendance` | Check-in/check-out + upsert by status |
| `/performance-reviews` | Review CRUD (1-5 rating) |

### Finance (`/api/farm/{farmId}/finance`)

| Resource | Description |
|---|---|
| `/finance/expenses` | Expense records (soft-delete, auto-linked from medical/vaccination) |
| `/finance/income-records` | Income records (auto-linked from customer sales) |
| `/finance/expense-categories` | Expense category management |
| `/finance/income-categories` | Income category management |
| `/finance/payment-methods` | Payment method management |
| `/finance/reports/profit-loss` | Profit/loss report |
| `/finance/reports/expense-breakdown` | Expense breakdown by category |
| `/finance/reports/income-breakdown` | Income breakdown by category |
| `/finance/reports/monthly-summary` | Monthly income vs expenses |

### Inventory (`/api/farm/{farmId}/inventory-items`, `/inventory/*`)

| Resource | Description |
|---|---|
| `/inventory-items` | Item CRUD + movements + reports |
| `/inventory-items/movements` | Stock movement history |
| `/inventory/suppliers` | Supplier management + purchase records |
| `/inventory/customers` | Customer management + sale records |

### Tasks (`/api/farm/{farmId}/tasks`)

| Method | Path | Description |
|---|---|---|
| CRUD | `/tasks` | Farm task management |
| POST | `/tasks/{id}/start` | Move to In Progress |
| POST | `/tasks/{id}/complete` | Mark complete |
| POST | `/tasks/{id}/cancel` | Cancel |
| POST | `/tasks/{id}/reopen` | Reopen cancelled task |

### Dashboard & Reports

| Resource | Description |
|---|---|
| `/dashboard/summary` | Dashboard statistics (animal counts, stock values, due counts) |
| `/dashboard/alerts` | Active alerts (overdue vaccinations, due weight checks, medicine alerts, overdue tasks, upcoming births, low inventory) — each includes a `link` to the relevant page |
| `/dashboard/charts` | Chart data with configurable date range |
| `/reports/animals` | Animal reports |
| `/reports/medical` | Medical reports |
| `/reports/vaccination` | Vaccination reports |
| `/reports/employees` | Employee reports |

### Configuration & Admin

| Resource | Description |
|---|---|
| `/configuration/animal-types` | Animal type CRUD |
| `/configuration/breeds` | Breed CRUD (linked to animal type) |
| `/configuration/sex-options` | Sex option CRUD |
| `/configuration/age-categories` | Age category CRUD (min/max days) |
| `/configuration/statuses` | Animal status CRUD (system-defined + custom) |
| `/configuration/identification-types` | Identification type CRUD |
| `/configuration/location-types` | Location type CRUD |
| `/configuration/locations` | Hierarchical location CRUD |
| `/configuration/custom-fields` | Custom field definition CRUD |
| `/configuration/farm-config` | Farm config key-value pairs |
| `/audit-logs` | Audit log with entity/user/action/date filters — SystemOwner/FarmManager/Accountant only |
| `/api/admin/jobs` | Scheduled job status: recurring jobs, last run/state/error, recent failures — SystemOwner account role only. Account-scoped (no `{farmId}`), so the farm-context gate deliberately does not apply |
| `/notifications` | The caller's own notifications, with read/dismiss state. Farm-scoped and self-scoped: the recipient comes from the token, never the request |
| `/notifications/unread-count` | Unread count for the header badge |
| `/notifications/{id}/read`, `/notifications/read-all`, `/notifications/{id}/dismiss` | Acknowledge or clear a notification. Another member's id answers 404 |
| `/notifications/preferences` | Per alert type, in-app and email channels (`Notifications` configuration section holds the default email threshold) |

Background jobs run through Hangfire against the same PostgreSQL database (`hangfire` schema, created on startup after EF migrations). Recurring jobs fan out one execution per active farm: `feeding-task-generation-fan-out` (daily), `health-status-recalculation-fan-out` (hourly) and `notification-dispatch-fan-out` (every 15 minutes). See the `Jobs` configuration section, and `Jobs:Enabled=false` to run an instance that serves HTTP only.

Notification dispatch reuses the dashboard's alert computation and reconciles rather than appends: a new condition notifies each eligible recipient once, a changed one refreshes its row, and a cleared one is resolved. A recipient's own role decides whether an alert type reaches them at all (an Accountant is never alerted about inventory), and a condition whose metric failed is skipped rather than resolved — "we could not tell" is not "it is fixed".

## Roles

Seeded on startup: **SystemOwner**, **FarmManager**, **Veterinarian**, **Employee**, **Accountant**, **Viewer**

Some endpoints are role-restricted (e.g., breeding writes require Vet/FarmManager/SystemOwner, audit logs require SystemOwner/FarmManager/Accountant).

## Deployment

Production API: `https://fms-api-3ba95327d590.herokuapp.com/api`

Deployed via GitHub Actions to Heroku container registry on every push to `main`. See `.github/workflows/deploy.yml`.
