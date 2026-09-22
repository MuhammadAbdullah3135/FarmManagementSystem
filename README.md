# Farm Management System (FMS)

A full-stack farm management platform for livestock operations. Track animals, health records, breeding, feed, inventory, employees, and finances — all from a single dashboard.

**Live site:** https://muhammadabdullah3135.github.io/FarmManagementSystem/

> **Android APK:** No APK is published to GitHub Releases yet. Once a release exists, downloads will appear at **[github.com/MuhammadAbdullah3135/FarmManagementSystem/releases](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/releases)** — to set that up, follow GitHub's guide to [creating a release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository#creating-a-release) and attach the signed APK. Until then, build it yourself using the instructions in [src/FMS.Mobile](src/FMS.Mobile/).

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend API** | .NET 8 / ASP.NET Core, Entity Framework Core, PostgreSQL (Neon) |
| **Frontend** | React 19, TypeScript 6, Vite 8, Ant Design 6, Recharts |
| **Mobile** | .NET MAUI Android (WebView wrapper), Firebase Crashlytics (optional) |
| **Auth** | JWT (HMAC-SHA256), BCrypt password hashing, refresh tokens |
| **Deployment** | API on Heroku (Docker), frontend on GitHub Pages |
| **Database** | PostgreSQL with auto-migrations on startup |
| **Testing** | xUnit, EF Core InMemory, ASP.NET Core `TestWebApplicationFactory` |

## Architecture

```
+--------------+    +------------------+    +--------------+
|  Mobile App  |--->|  React SPA       |--->|  .NET API    |
|  (MAUI/      |    |  (GitHub Pages)  |    |  (Heroku)    |
|   Android)   |    |                  |    |              |
+--------------+    +------------------+    +------+-------+
                                                 |
                                          +------+-------+
                                          |  PostgreSQL  |
                                          |  (Neon)      |
                                          +--------------+
```

The backend follows Clean Architecture with four layers:

```
FMS.API -> FMS.Infrastructure -> FMS.Application -> FMS.Domain
```

| Project | Purpose |
|---|---|
| `src/FMS.API` | ASP.NET Core controllers, middleware, DI configuration |
| `src/FMS.Application` | Service interfaces, DTOs, `Result<T>` pattern |
| `src/FMS.Domain` | Entities, enums, base classes (zero dependencies) |
| `src/FMS.Infrastructure` | EF Core DbContext, service implementations, auth |
| `src/FMS.Mobile` | .NET MAUI Android WebView wrapper (no duplicate UI code) |
| `client/` | React SPA (Vite + Ant Design) |

## Features

All features below are implemented and verified against the source code. If something is partially implemented, it is noted as such.

### Farms & Multi-tenancy
- Multiple farms per account; the backend `FarmsController` supports full farm CRUD (create, read, update, delete)
- Client-side farm switcher in the header; every farm-scoped request carries an `X-Farm-Id` header that the API validates against farm membership
- **Partially implemented:** the client store exposes a `createFarm` action, but no page yet lets users create, rename, or delete a farm — only switch between existing ones

### Animal Management
- Full CRUD for animals with tag number, name, type, breed, sex, age category, status, location, sire/dam, date of birth, acquisition date, notes
- Animal identifications (type/value/primary flag)
- Status changes with reason tracking
- Weight records per animal (CRUD + weight report with ADG calculation)
- Animal timeline (aggregated events across all modules)
- Animal images and documents (upload/download, 5MB image / 20MB document limits)
- Animal transfers between locations
- Bulk status changes and bulk transfers
- Bulk import from CSV or Excel: column mapping (including a fixed value for every row), a per-row validation preview that writes nothing, and an all-or-nothing commit — rows are checked with the same rules the single-animal create endpoint uses, duplicate tags are reported rather than skipped or overwritten, and sire/dam may reference another row of the same file. The same import (same wizard, same pipeline, same semantics) also serves **employees** — departments and roles resolved by name, the email as the identifier — and **inventory items**, where the item name is the identifier

### Breeding
- Breeding records (sire, dam, date, method, vet, result)
- Gestation tracking (confirm pregnancy, revert, health checks with weight/vet)
- Birth recording (dynamic offspring rows with sex, outcome, birth weight, name)
- Lineage tree (configurable ancestor/descendant depth)
- Breeding reports: summary, trend, method distribution, sire performance, calendar view

### Feed Management
- Feed types with categories (Forage, Concentrate, Mineral, Supplement, Additive, Other) and configurable units
- Feed inventory stock tracking with purchase/consumption/adjustment movements
- Feed records (per-animal or per-location targeting)
- Diet plans with itemized feed compositions and animal-type/breed/age/weight targeting
- Feeding schedules (time-of-day based)
- Auto-generated feeding tasks from schedules, with completion and skip tracking
- Feed reports: consumption trends, by feed type, by animal, by location, cost summary

### Health Management
- Medical records (symptoms, diagnosis, treatment, medicine, dosage, vet, cost, follow-up, status)
- Medicine inventory with stock batches (batch number, quantity, unit cost, expiry, supplier)
- FIFO medicine usage deduction from stock batches
- Medicine alerts: low stock, expired, expiring soon
- Vaccine types (with linked medicine for automatic stock deduction)
- Vaccination records (per-animal, with batch number, vet, cost, auto-linked expense)
- Vaccination schedules (recurrence-based, with overdue/due/upcoming status)
- Weight check schedules (by animal type/breed/age category, with overdue detection)
- Health cost reports: summary, by vet, by animal, by month

### HR & Employees
- Employee management (name, phone, email, department, role, salary type/rate, hire date)
- Department and role management
- Salary payment tracking with payroll reports (including annualized run-rate)
- Attendance (check-in/check-out, upsert by status: Present/Absent/Late/HalfDay/Leave/Holiday)
- Performance reviews (1-5 rating, strengths, areas for improvement, reviewer)

### Finance
- Expense and income tracking with categories and payment methods
- Animal-linked or location-linked expense allocation
- Automatic expense creation from medical records and vaccinations
- Profit/loss reports, expense/income breakdowns, monthly summaries

### Inventory
- Inventory items with category, unit, reorder level, unit cost, location
- Stock movements (Purchase, Consumption, Transfer, Adjustment)
- Supplier management with purchase records (auto-linked to expenses and stock)
- Customer management with sale records (auto-linked to stock reduction and income)
- Inventory reports with low-stock alerts

### Farm Tasks
- Task CRUD with priority (Low/Medium/High), due date, assignee, animal/location links
- State machine: Pending -> In Progress -> Completed/Cancelled -> Reopen
- Filter by status, priority, assignee, overdue, search text
- Automatic timeline events on state transitions

### Dashboard
- Summary statistics: total animals, pregnant, sick, due weight checks, overdue tasks, upcoming births, feed/inventory stock values
- Alerts: overdue vaccinations, due weight checks, medicine alerts, overdue tasks, upcoming births, low inventory (sorted by severity)
- Charts: animal trends, expense breakdown (pie), feed consumption trend, monthly P/L (configurable date range)

### Reports
- Cross-domain report pages: animal, medical, vaccination, employee, breeding, feed, finance
- **Cost per animal / per herd**: what each animal cost and earned over a date range, with shared costs allocated by animal-days — every allocated figure shows the days, the pool and the fraction it came from, unallocated money is reported as unallocated, and the arithmetic that ties the totals back to the finance, feed and health reports is shown alongside the numbers
- Export to PDF, Excel, or CSV from any report table

### Configuration
- Animal types, breeds, sex options, age categories, statuses (system-defined + custom)
- Identification types, location types, hierarchical locations
- Custom field definitions (String/Number/Boolean/Date/Select)
- Farm configuration key-value pairs
- Every configurable list can be created **inline from the form that needs it** — a missing expense category, breed, location, department or status is added in place, so an entry form is never abandoned half-filled and re-started. One registry maps each of the 17 configurable lookups to its modal fields and create call, so a form declares which lookup a field needs instead of each page hand-writing a modal and a create handler

### Admin
- Audit log with filters (entity type, user, action, date range) — all CUD operations automatically logged via EF Core interceptor

### Authentication & Authorization
- Registration (creates account + user + default farm, then seeds the full default lookup set — animal types, breeds, statuses, locations, categories, and more)
- Login with JWT access + refresh tokens (15 min / 30 days)
- Password reset flow
- Role-based access: SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer
- Farm-scoped data isolation via `X-Farm-Id` header + middleware validation
- Rate limiting (300 requests/minute per user/IP)
- Security headers (CSP, X-Frame-Options, X-Content-Type-Options, etc.)

### Offline (in progress)

- The frontend registers a service worker that precaches its own built shell, so the Android wrapper opens the app without a connection instead of a blank screen. It is scoped to the app's base path and skipped in dev.
- A per-account, per-farm IndexedDB store keeps what the device has fetched, with the last-synced time reported in an offline banner that says plainly what the device can and cannot do.
- Signing out empties the store, and losing access to a farm drops that farm's data — the device never keeps serving rows it can no longer authorise.
- Cached reads and the write queue are **not built yet**, so an offline session is currently the shell, the cache and honest messaging rather than usable data. The approved design, its increments and their order are in [docs/OFFLINE.md](docs/OFFLINE.md).

## Screenshots

> TODO: Add screenshots of the dashboard, animal management, and other key screens here.
> To add: place images in a `docs/screenshots/` folder and reference them below.
>
> ![Dashboard](docs/screenshots/dashboard.png)

## Testing

The project includes unit tests, integration tests, and end-to-end API tests under `tests/FMS.Domain.Tests/`.

```bash
dotnet test
```

### Test coverage

| Module | Test files | What's tested |
|---|---|---|
| Animals | `AnimalServiceTests`, `AnimalTimelineTests`, `WeightServiceTests` | CRUD, duplicate guards, weight reports, timeline, soft delete |
| Breeding | `CrossModuleDateAndLinkTests` | Offspring linkage, birth/breeding validation, date preservation |
| Feed | `FeedServiceTests`, `FeedRecordingTests`, `FeedPlanningTests`, `FeedReportTests` | Stock movements, diet plans, schedule/task generation, consumption reports |
| Health | `MedicalRecordServiceTests`, `MedicineServiceTests`, `VaccineServiceTests`, `HealthCostServiceTests`, `WeightCheckScheduleServiceTests`, `MedicalRecordOrphanAndLinkTests`, `VaccinationLinkedMedicineTests` | CRUD, FIFO stock deduction, cost aggregation, schedule status, cross-module expense linking |
| HR | `EmployeeServiceTests`, `SalaryPaymentTests`, `PerformanceTests`, `AttendanceTests` | CRUD, validations, payroll reports, check-in/out, upsert |
| Finance | `FinanceServiceTests` | Expense/income CRUD, P&L reports, breakdowns, soft-delete exclusion |
| Tasks | `FarmTaskTests` | CRUD, state machine, filters, overdue detection |
| Dashboard | `DashboardE2ETests` | Summary stats, alerts, charts, cross-farm isolation, auth |
| Reports | `ReportsE2ETests`, `CostReportE2ETests` | Animal/medical/vaccination/employee reports, date filtering, cross-farm isolation |
| Cost attribution | `CostAttributionServiceTests` | Hand-computed animal-day allocation, location pools, mid-range transfers, departures, pool rounding adds up exactly, health cost counted once, reconciliation identities, incomplete-data caveats, farm isolation |
| Audit | `AuditLogInterceptorTests` | Create/update logging, old/new values, multi-entity save |
| Auth | `RoleAndIsolationE2ETests`, `FarmContextAuthorizationE2ETests` | Farm-role authorization matrix (real middleware), cross-farm header/route isolation |
| Cache | `ConfigurationCacheTests` | Breed caching per animal type |
| Configuration | `ConfigurationDeleteGuardTests`, `ConfigurationDeleteE2ETests`, `FarmDefaultsSeederTests` | In-use lookup delete guards, default data seeding |
| Middleware | `GlobalExceptionMiddlewareTests` | RFC 7807 ProblemDetails mapping |
| Password reset | `PasswordResetE2ETests` | Request/confirm reset flow |
| Bulk import | `SpreadsheetReaderTests`, `AnimalImportServiceTests`, `InventoryImportServiceTests`, `EmployeeImportServiceTests`, `AnimalImportControllerTests`, `ImportControllerTests`, `ImportOptionsBindingTests`, `AnimalImportE2ETests`, `ImportE2ETests` | CSV (including this app's own export shape) and `.xlsx` parsing, header auto-detection and explicit/constant mappings, ambiguous-date refusal, lookup and parent-tag resolution inside and across farms (including a name matching two records), duplicate-identifier and soft-delete parity with each entity's create endpoint in every variant, named-field parity with the endpoint's own messages, the column caps that used to surface as 500s, employee email uniqueness case/padding-insensitively, all-or-nothing commit with a revalidation from the file, file/row-size limits, the legacy `AnimalImport` keys still applying, and farm-context enforcement over HTTP for all three routes |
| Scheduled jobs | `FeedingTaskGenerationJobTests`, `HealthStatusRecalculationJobTests`, `FarmJobSchedulerTests`, `FarmDirectoryTests`, `BackgroundJobsSetupTests`, `JobsE2ETests` | Job output equals the manual endpoint, idempotency, per-farm isolation, failure logging and rethrow, snapshot freshness/fallback, job-status endpoint authorization |
| Notifications | `NotificationDispatcherTests`, `NotificationPreferenceTests`, `NotificationDispatchJobTests`, `NotificationApiE2ETests` | Exactly one notification per condition and recipient across repeated runs, refresh-in-place when a condition changes, resolve-then-re-notify when it recurs, audience filtering by farm role, a disabled channel meaning no work at all, delivery failures recorded rather than thrown, skipped alert types when a metric fails, recipient isolation and farm-context enforcement over HTTP |
| Email delivery | `SendGridEmailTransportTests`, `EmailServiceRenderingTests`, `EmailConfigurationGuardTests`, `EmailCallerContinuationTests`, `EmailConfigurationDriftTests`, `RealEmailDeliveryTests` | Retry-with-backoff on 429/408/5xx honouring a capped `Retry-After`, no retry on 400/401/403, the API key never appearing in a log or exception on any path, relative digest links absolutised against `Frontend:BaseUrl`, HTML escaping of farm names and alert text, the guard refusing a provider it cannot use outside Development and downgrading to the log transport inside it, and reset/invitation still succeeding when every send throws. Live delivery to a real inbox is gated — see below |

Tests use EF Core InMemory for isolation. E2E tests use `TestWebApplicationFactory` with a full HTTP pipeline, seeded test data, and JWT authentication.

The client has its own Vitest suite (`cd client && npm test`), alongside `tsc`, `oxlint` and
`vite build`. It covers the offline shell in particular — the IndexedDB store, its schema
migration step and its clearing policy against a real IndexedDB implementation, the
connectivity store, the service-worker template and the build-time precache guard, and the
banner and layout wiring that uses them. It also covers the inline quick-add registry — each
lookup kind's real API wiring, the select's create/select/error contract, a source-level guard
that every configurable field is bound to the expected kind, and end-to-end flows for expenses,
suppliers and the configuration page — and the responsive rules that keep card heads from
truncating their titles or overflowing at narrower widths.

A handful of tests need a real PostgreSQL server, because InMemory cannot stand in
for it: the dashboard metric calculations, and the Hangfire job-storage schema and
recurring-job registration. They SKIP (never fail) when no server is reachable.
Point them at a writable server with `FMS_TEST_POSTGRES` to actually run them —
`Skipped: 0` is the pass condition:

```bash
export FMS_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --nologo
```

The role needs `CREATEDB`: these tests create and drop throwaway databases. See
[docs/VERIFICATION.md](docs/VERIFICATION.md) for this and the other
environment-dependent checks, with their status and runbooks.

Live email delivery is gated the same way — three tests send one message each
(password reset, invitation, alert digest) and skip unless a recipient and
credentials are supplied, because an automated run should not send mail by itself:

```bash
export FMS_TEST_EMAIL="you@example.com"
export FMS_TEST_EMAIL_PROVIDER=SendGrid
export FMS_TEST_EMAIL_FROM="noreply@your-verified-sender.com"
export FMS_TEST_SENDGRID_API_KEY="SG.…"
export FMS_TEST_FRONTEND_URL="https://your-frontend.example.com"
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --filter FullyQualifiedName~RealEmailDeliveryTests
```

## Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — API and tests
- [Node.js 22+](https://nodejs.org/) — frontend
- [PostgreSQL](https://www.postgresql.org/) (or a [Neon](https://neon.tech/) cloud database)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) + .NET MAUI workload + Android SDK (mobile builds only)

### API Backend

```bash
# Set connection string (or use appsettings.json / .env)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=fms;Username=postgres;Password=..."

# Run from repo root
dotnet run --project src/FMS.API
```

The API starts at `http://localhost:5200`. Swagger UI is available at `/swagger` in development. Database migrations and role seeding run on first startup.

Health check endpoints:
- `GET /health` — full check (includes database connectivity)
- `GET /health/ready` — readiness probe (database only)
- `GET /health/live` — liveness probe (always healthy)

Scheduled background work (Hangfire, job tables in the `hangfire` schema of the same PostgreSQL database, cron evaluated in UTC):
- `feeding-task-generation-fan-out` (default `5 0 * * *`) — materialises each active farm's feeding tasks for the current day by calling the same service the manual endpoint uses;
- `health-status-recalculation-fan-out` (default `0 * * * *`) — refreshes each farm's due/overdue vaccination and weight-check snapshot, which the dashboard reads as a fast path with a live-calculation fallback;
- `notification-dispatch-fan-out` (default `*/15 * * * *`) — evaluates each farm's alert conditions (the same six the dashboard computes) and reconciles one notification per recipient, then emails a digest for anything newly raised.

All three fan out one job **per farm**, so a farm's work runs in its own scope and retries independently. Configure with the `Jobs` section (see `.env.example`); `Jobs__Enabled=false` runs the API with no scheduler in that process.

Notifications are durable rather than page-load-only, and deduplicated on the identity of the condition: an alert that merely changes (a pushed-back due date) refreshes its row, one that clears is resolved, and one that recurs alerts again. Only genuinely new conditions are emailed, and email defaults to Critical severity only — `Notifications:EmailMinSeverityOnByDefault` changes that default. Push and SMS are not implemented.

Job status for SystemOwners:
- `GET /api/admin/jobs` — recurring jobs, next/last run, last state, last error and recent failures (account-scoped, so no farm context is required).
- `/hangfire` — Hangfire's own dashboard, mounted in Development only (a browser cannot carry the SPA's bearer token, so it is not exposed in production).

Notification centre (farm-scoped, always your own notifications):
- `GET /api/farm/{farmId}/notifications` — list, with `unreadOnly`, `includeDismissed`, `includeResolved` and paging;
- `GET /api/farm/{farmId}/notifications/unread-count` — what the header badge shows;
- `POST /api/farm/{farmId}/notifications/{id}/read`, `POST …/read-all`, `POST …/{id}/dismiss`;
- `GET`/`PUT /api/farm/{farmId}/notifications/preferences` — per alert type, in-app and email.

### Email delivery

The API sends three emails — password reset, farm invitation, and one alert digest per
recipient — through the `IEmailService` contract, whose shape has not changed. Which
transport delivers them is configuration:

| `Email:Provider` | Behaviour |
|---|---|
| `Log` (default) | The message is rendered and written to the application log. Nothing is delivered. This is what a fresh clone, the test suite and Development use: no credentials needed, and a reset link can be copied out of the log. |
| `SendGrid` | Delivered through SendGrid's v3 HTTP API (a single JSON POST, no SDK). |

To turn delivery on (see `.env.example` for the full annotated list):

```bash
export Email__Provider=SendGrid
export Email__FromAddress="noreply@your-verified-sender.com"   # must be verified with the provider
export Email__SendGrid__ApiKey="SG.…"                          # "Mail Send" permission is enough
export Frontend__BaseUrl="https://your-frontend.example.com"    # every email links back here
docker compose up --build   # or: dotnet run --project src/FMS.API
```

With no domain, verify a single sender address (SendGrid → Settings → Sender
Authentication → Single Sender Verification); verifying a whole domain with SPF/DKIM is
better for deliverability once you have one. Then run the gated live tests under
**Testing** above to confirm a real inbox receives all three flows.

Two guards make a misconfiguration loud rather than silent:

- `EmailConfigurationGuard` **refuses to start** outside Development when a provider is
  selected but its key, sender or sender format is unusable. Without it, every password
  reset and invitation would be accepted, logged as sent, and never delivered — and
  because those flows deliberately keep working when a send fails, nothing else would
  reveal it. In Development the same problem downgrades to the `Log` transport with a
  warning instead of failing.
- It **warns** when `Frontend:BaseUrl` is missing or still the shipped `yourdomain.com`
  template, because every email contains a link back into the frontend and a template URL
  produces real emails whose links go nowhere.

Delivery failures do not fail the request. The reset token and the invitation row are
persisted before the send, so a provider outage means "nobody got the mail" — visible in
structured logs, recoverable by asking again — rather than a lost token or a 500 on the
login page. This is deliberately the same asymmetry the notification dispatcher already
used for the alert digest. The transport throws; the caller decides, and the two callers
log and continue.

### React Frontend

```bash
cd client
npm install
npm run dev
```

Opens at `http://127.0.0.1:3000`. The dev server proxies `/api` requests to the backend at `http://localhost:5200`.

### Android Mobile

```bash
# Requires Android SDK + .NET MAUI workload
dotnet workload install maui
dotnet build src/FMS.Mobile/FMS.Mobile.csproj -f net10.0-android

# APK output at:
# src/FMS.Mobile/bin/Debug/net10.0-android/android-arm64/com.fms.mobile-Signed.apk
```

Once the app has loaded on the device at least once, it opens without a connection: the
web app precaches itself, so the wrapper's native overlay now exists only for a first
launch that could not fetch the shell at all. See
[src/FMS.Mobile/README.md](src/FMS.Mobile/README.md) for full build details, Firebase setup
and known limitations, and [docs/VERIFICATION.md](docs/VERIFICATION.md) for the device
checks on offline behaviour that this environment cannot run.

## Project Structure

```
FarmManagementSystem/
├── src/
│   ├── FMS.API/              # ASP.NET Core Web API (controllers, middleware, config)
│   ├── FMS.Application/      # Service interfaces, DTOs, Result<T> pattern
│   ├── FMS.Domain/           # Entities, enums, base classes
│   ├── FMS.Infrastructure/   # EF Core, service implementations, auth
│   └── FMS.Mobile/           # .NET MAUI Android WebView wrapper
├── client/                   # React SPA (Vite + Ant Design)
├── tests/
│   └── FMS.Domain.Tests/     # Unit, integration, and E2E tests
├── docs/                     # User guide, API docs
├── scripts/                  # Database backup/restore, plus the live-API QA scripts
│                             #   (qa-survey, qa-seed, qa-smoke — see docs/VERIFICATION.md)
├── Dockerfile                # API container build (multi-stage)
├── docker-compose.yml        # Local dev: API + PostgreSQL
├── .github/workflows/        # CI/CD: Heroku deploy + GitHub Pages
└── FarmManagementSystem.slnx # Solution file
```

> **Note (archived Sept 2026):** An early-stage Next.js 15 + Supabase monorepo (`rewrite/`) was evaluated as a possible successor to the React SPA and **deliberately not pursued**. It had reached only an authentication shell plus a hand-written database schema — no data layer, no business logic, and no tests — and could not host the API's domain logic (RLS is row filtering only). It was removed from `main` so the project has a single source of truth for roles, domain models, and schema instead of two that had already begun to drift.
>
> The complete directory, including the `packages/shared` types/validators and `packages/ui` components, is preserved at git tag **`rewrite-initial`** (branch `archive/rewrite-full`) should the decision ever be revisited.

## CI/CD

| Workflow | Trigger | What it does |
|---|---|---|
| `deploy.yml` | Push to `main`, pull request to `main`, or manual dispatch | Builds Docker image, deploys to Heroku container registry |
| `pages-deploy.yml` | Push to `main` (when `client/**` changes), pull request to `main`, or manual dispatch | Builds React SPA, deploys to GitHub Pages |

> **Note:** Both workflows run their test suites as a gate before the deploy job starts — the API workflow runs `dotnet test tests/FMS.Domain.Tests`, and the Pages workflow runs lint, build, and `npm test` — so a failing suite blocks the deploy. You can still run `dotnet test` locally to catch issues earlier.
>
> Pull requests run the same suites, and each deploy job is guarded with
> `if: github.event_name != 'pull_request' && github.ref == 'refs/heads/main'`, so a green PR
> proves the suite passes and never publishes. A manual dispatch on any other ref is safe for the
> same reason (`gh workflow run deploy.yml --ref <branch>`). Note that GitHub takes the workflow
> definition from the **base** branch for `pull_request` events, so this gate applies once the
> change is on `main`.

## License

Not currently licensed. Contact the repository owner for usage terms.
