# Farm Management System (FMS)

A full-stack farm management platform for livestock operations. Track animals, health records, breeding, feed, inventory, employees, and finances — all from a single dashboard.

**Live site:** https://muhammadabdullah3135.github.io/FarmManagementSystem/

> **Android APK:** Not currently hosted on GitHub Releases. See [src/FMS.Mobile](src/FMS.Mobile/) for build instructions, or [create a release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository#creating-a-release) to upload a prebuilt APK.

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

### Animal Management
- Full CRUD for animals with tag number, name, type, breed, sex, age category, status, location, sire/dam, date of birth, acquisition date, notes
- Animal identifications (type/value/primary flag)
- Status changes with reason tracking
- Weight records per animal (CRUD + weight report with ADG calculation)
- Animal timeline (aggregated events across all modules)
- Animal images and documents (upload/download, 5MB image / 20MB document limits)
- Animal transfers between locations
- Bulk status changes and bulk transfers

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
- Export to PDF, Excel, or CSV from any report table

### Configuration
- Animal types, breeds, sex options, age categories, statuses (system-defined + custom)
- Identification types, location types, hierarchical locations
- Custom field definitions (String/Number/Boolean/Date/Select)
- Farm configuration key-value pairs

### Admin
- Audit log with filters (entity type, user, action, date range) — all CUD operations automatically logged via EF Core interceptor

### Authentication & Authorization
- Registration (creates account + user + default farm + seeds statuses)
- Login with JWT access + refresh tokens (15 min / 30 days)
- Password reset flow
- Role-based access: SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer
- Farm-scoped data isolation via `X-Farm-Id` header + middleware validation
- Rate limiting (300 requests/minute per user/IP)
- Security headers (CSP, X-Frame-Options, X-Content-Type-Options, etc.)

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
| Reports | `ReportsE2ETests` | Animal/medical/vaccination/employee reports, date filtering, cross-farm isolation |
| Audit | `AuditLogInterceptorTests` | Create/update logging, old/new values, multi-entity save |
| Auth | `RoleAndIsolationE2ETests` | Role authorization matrix, cross-farm header/route isolation |
| Cache | `ConfigurationCacheTests` | Breed caching per animal type |

Tests use EF Core InMemory for isolation. E2E tests use `TestWebApplicationFactory` with a full HTTP pipeline, seeded test data, and JWT authentication.

## Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 22+](https://nodejs.org/)
- [PostgreSQL](https://www.postgresql.org/) (or a [Neon](https://neon.tech/) cloud database)
- Android SDK (for mobile builds only)

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

See [src/FMS.Mobile/README.md](src/FMS.Mobile/) for full build details, Firebase setup, and known limitations.

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
├── rewrite/                  # Next.js + Supabase rewrite (early stage, not active)
├── docs/                     # User guide, API docs
├── scripts/                  # Database backup/restore scripts
├── Dockerfile                # API container build (multi-stage)
├── docker-compose.yml        # Local dev: API + SQL Server
├── .github/workflows/        # CI/CD: Heroku deploy + GitHub Pages
└── FarmManagementSystem.slnx # Solution file
```

> **Note:** The `rewrite/` directory contains an early-stage Next.js 15 + Supabase monorepo intended to eventually replace the React SPA. It currently only has authentication pages and a dashboard shell built. It is not active or deployed.

## CI/CD

| Workflow | Trigger | What it does |
|---|---|---|
| `deploy.yml` | Push to `main` | Builds Docker image, deploys to Heroku container registry |
| `pages-deploy.yml` | Push to `main` (when `client/**` changes) | Builds React SPA, deploys to GitHub Pages |

> **Note:** Neither workflow runs tests before deploying. Tests should be run locally with `dotnet test` before pushing.

## License

Not currently licensed. Contact the repository owner for usage terms.
