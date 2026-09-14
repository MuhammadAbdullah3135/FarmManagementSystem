# Farm Management System (FMS)

A full-stack farm management platform for livestock operations. Track animals, health records, breeding, feed, inventory, employees, and finances — all from a single dashboard.

**Live site:** https://muhammadabdullah3135.github.io/FarmManagementSystem/

> The Android APK is not currently hosted on GitHub Releases. See [src/FMS.Mobile](src/FMS.Mobile/) for build instructions, or [create a release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository#creating-a-release) to upload one.

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend API** | .NET 8 / ASP.NET Core, Entity Framework Core, PostgreSQL (Neon) |
| **Frontend** | React 19, TypeScript, Vite 8, Ant Design 6, Recharts |
| **Mobile** | .NET MAUI Android (WebView wrapper), Firebase Crashlytics |
| **Auth** | JWT (HMAC-SHA256), BCrypt password hashing, refresh tokens |
| **Deployment** | API on Heroku (Docker), frontend on GitHub Pages |
| **Database** | PostgreSQL with auto-migrations on startup |

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

The mobile app is a thin WebView wrapper around the same React frontend — no duplicate UI code.

## Features

All features below are implemented and verified against the source code. If something is partially implemented, it is noted as such.

### Animal Management
- Full CRUD for animals with tag number, name, type, breed, sex, age category, status, location, sire/dam, date of birth, acquisition date, notes
- Animal identifications (type/value/primary flag)
- Status changes with reason tracking
- Weight records per animal (CRUD + weight report)
- Animal timeline (aggregated events)
- Animal images and documents (upload/download with file size limits)
- Animal transfers between locations
- Bulk status changes and bulk transfers

### Breeding
- Breeding records (sire, dam, date, method, vet, notes, result)
- Gestation tracking (confirm pregnancy, revert, health checks with weight/vet)
- Birth recording (dynamic offspring rows with sex, outcome, birth weight, name)
- Lineage tree (configurable ancestor/descendant depth)
- Breeding reports: summary, trend, method distribution, sire performance, calendar view

### Feed Management
- Feed types with categories (Forage, Concentrate, Mineral, Supplement, Additive, Other) and units
- Feed inventory stock tracking with purchase/consumption/adjustment movements
- Feed records (per-animal or per-location targeting)
- Diet plans with itemized feed compositions and animal-type targeting
- Feeding schedules (time-of-day based)
- Feeding task generation, completion, and skip tracking
- Feed reports: consumption trends, by feed type, by animal, by location, cost summary

### Health Management
- Medical records (symptoms, diagnosis, treatment, medicine, dosage, vet, cost, follow-up, status)
- Medicine inventory with stock batches (batch number, quantity, unit cost, expiry, supplier)
- Medicine usage logging and expiry/low-stock alerts
- Vaccine types (with linked medicine)
- Vaccination records (per-animal, with batch number, vet, cost)
- Vaccination schedules (recurrence-based, with overdue status)
- Weight check schedules (by animal type/breed/age category)
- Health cost reports: summary, by vet, by animal, by month

### HR & Employees
- Employee management (name, phone, email, department, role, salary type/rate, hire date)
- Department and role management
- Salary payment tracking with payroll reports
- Attendance (check-in/check-out, upsert by status: Present/Absent/Late/HalfDay/Leave/Holiday)
- Performance reviews (rating, strengths, areas for improvement)

### Finance
- Expense and income tracking with categories and payment methods
- Animal-linked or location-linked expense allocation
- Profit/loss reports, expense/income breakdowns, monthly summaries

### Inventory
- Inventory items with category, unit, reorder level, unit cost, location
- Stock movements (Purchase, Consumption, Transfer, Adjustment)
- Supplier management with purchase records
- Customer management with sale records (auto-linked stock reduction + income)
- Inventory reports

### Farm Tasks
- Task CRUD with priority (Low/Medium/High), due date, assignee
- State machine: Pending -> In Progress -> Completed/Cancelled -> Reopen
- Filter by status, priority, assignee, overdue

### Dashboard
- Summary statistics, alerts, and charts (configurable date range)

### Reports
- Animal, medical, vaccination, employee, breeding, feed, and finance report pages
- Export to PDF, Excel, or CSV from any report table

### Configuration
- Animal types, breeds, sex options, age categories, statuses
- Identification types, location types, locations
- Custom field definitions, farm config key-value pairs

### Admin
- Audit log with filters (entity type, user, action, date range)

### Authentication & Authorization
- Registration (creates account + user + default farm + seeds statuses)
- Login with JWT access + refresh tokens (15 min / 30 days)
- Password reset flow
- Role-based access: SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer
- Farm-scoped data isolation via `X-Farm-Id` header

## Screenshots

> TODO: Add screenshots of the dashboard, animal management, and other key screens here.
> To add: place images in a `docs/screenshots/` folder and reference them below.
>
> ![Dashboard](docs/screenshots/dashboard.png)

## Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 22+](https://nodejs.org/)
- [PostgreSQL](https://www.postgresql.org/) (or a Neon cloud database)
- Android SDK (for mobile builds)

### API Backend

```bash
# Set connection string (or use appsettings.json)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=fms;Username=postgres;Password=..."

# Run from repo root
dotnet run --project src/FMS.API
```

The API starts at `http://localhost:5200`. Swagger UI opens automatically. Database migrations and role seeding run on first startup.

### React Frontend

```bash
cd client
npm install
npm run dev
```

Opens at `http://127.0.0.1:3000`. The dev server proxies `/api` to the backend at `http://localhost:5200`.

### Android Mobile

```bash
# Requires Android SDK + .NET MAUI workload
dotnet workload install maui
dotnet build src/FMS.Mobile/FMS.Mobile.csproj -f net10.0-android

# APK output at:
# src/FMS.Mobile/bin/Debug/net10.0-android/android-arm64/com.fms.mobile-Signed.apk
```

See [src/FMS.Mobile/README.md](src/FMS.Mobile/) for full details.

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
│   └── FMS.Domain.Tests/     # Unit tests
├── rewrite/                  # In-progress Next.js + Supabase rewrite (not active)
├── Dockerfile                # API container build
├── docker-compose.yml        # Local dev: API + SQL Server
├── .github/workflows/        # CI/CD: Heroku deploy + GitHub Pages
└── FarmManagementSystem.slnx # Solution file
```

## License

Not currently licensed. Contact the repository owner for usage terms.
