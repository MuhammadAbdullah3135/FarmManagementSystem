# FMS API

ASP.NET Core 8 Web API backend for the Farm Management System. Provides all data operations, authentication, and business logic for the React frontend and mobile app.

## Tech

- **Framework:** ASP.NET Core 8 (minimal hosting, no Startup.cs)
- **ORM:** Entity Framework Core 8 with PostgreSQL (Npgsql)
- **Auth:** JWT (HMAC-SHA256) + BCrypt password hashing + refresh tokens
- **Validation:** FluentValidation (auto-scanned from assembly)
- **Logging:** Serilog
- **API docs:** Swagger/OpenAPI (Swashbuckle)
- **Rate limiting:** 300 requests/minute per user/IP

## Running Locally

```bash
# Set connection string (or edit appsettings.json)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=fms;Username=postgres;Password=..."

# Run
dotnet run --project src/FMS.API
```

- API: `http://localhost:5200`
- Swagger UI: `http://localhost:5200/swagger` (opens automatically in dev)
- Health check: `http://localhost:5200/health`

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

## Middleware Pipeline

1. `GlobalExceptionMiddleware` — RFC 7807 ProblemDetails error responses
2. Rate Limiter — 300 req/min per user/IP
3. Security Headers — X-Content-Type-Options, X-Frame-Options, CSP, etc.
4. Swagger (dev only)
5. CORS (configurable origins)
6. JWT Authentication
7. `FarmContextMiddleware` — validates `X-Farm-Id` header, sets farm context

## Key Endpoints

All farm-scoped endpoints require `Authorization: Bearer <token>` + `X-Farm-Id: <farmId>` headers.

### Auth (`/api/auth`)

| Method | Path | Description |
|---|---|---|
| POST | `/register` | Create account + user + default farm |
| POST | `/login` | Returns JWT access + refresh tokens |
| POST | `/refresh` | Swap refresh token for new pair |
| POST | `/reset-password` | Email-based password reset |
| GET | `/me` | Current user info |

### Animals (`/api/farm/{farmId}/animals`)

| Method | Path | Description |
|---|---|---|
| GET | `/` | List with filters (search, type, breed, status, location) |
| POST | `/` | Create animal |
| GET | `/{id}` | Animal detail |
| PUT | `/{id}` | Update animal |
| DELETE | `/{id}` | Delete animal |
| PUT | `/{id}/status` | Change status with reason |
| CRUD | `/{id}/weights` | Weight records |
| GET | `/{id}/timeline` | Event timeline |

### Breeding (`/api/farm/{farmId}/breeding-records`, `/gestation`, `/births`, `/lineage`)

| Method | Path | Description |
|---|---|---|
| CRUD | `/breeding-records` | Breeding records (sire, dam, method, result) |
| POST | `/gestation/confirm` | Confirm pregnancy |
| POST | `/gestation/{id}/revert` | Revert gestation |
| CRUD | `/gestation/{id}/health-checks` | Gestation health checks |
| CRUD | `/births` | Birth records with dynamic offspring |
| GET | `/lineage/{animalId}` | Ancestor/descendant tree |

### Feed (`/api/farm/{farmId}/feed/*`)

| Resource | Description |
|---|---|
| `/feed/types` | Feed type CRUD (Forage, Concentrate, Mineral, etc.) |
| `/feed/inventory` | Stock levels and movements |
| `/feed/records` | Per-animal or per-location feed records |
| `/feed/diet-plans` | Diet plans with itemized compositions |
| `/feed/schedules` | Time-based feeding schedules |
| `/feed/tasks` | Generate, complete, or skip feeding tasks |
| `/feed/reports` | Consumption trends, by type/animal/location, cost |

### Health (`/api/farm/{farmId}/medical-records`, `/medicines`, `/vaccines`, `/vaccinations`, `/weight-schedules`, `/health/costs`)

| Resource | Description |
|---|---|
| `/medical-records` | Medical records with status tracking |
| `/medicines` | Medicine CRUD + stock batches + usage + alerts |
| `/vaccines` | Vaccine type management |
| `/vaccinations` | Vaccination records + schedules + overdue status |
| `/weight-schedules` | Weight check schedules |
| `/health/costs` | Cost reports by vet, animal, month |

### HR (`/api/farm/{farmId}/employees`, `/departments`, `/employee-roles`, `/attendance`, `/performance-reviews`)

| Resource | Description |
|---|---|
| `/employees` | Employee CRUD + salary payments + payroll report |
| `/departments` | Department management |
| `/employee-roles` | Role management |
| `/attendance` | Check-in/check-out + upsert |
| `/performance-reviews` | Review CRUD |

### Finance (`/api/farm/{farmId}/finance`)

| Resource | Description |
|---|---|
| `/finance/expenses` | Expense records |
| `/finance/income-records` | Income records |
| `/finance/expense-categories` | Expense category management |
| `/finance/income-categories` | Income category management |
| `/finance/payment-methods` | Payment method management |
| `/finance/reports` | Profit/loss, breakdowns, monthly summary |

### Inventory (`/api/farm/{farmId}/inventory-items`, `/inventory/*`)

| Resource | Description |
|---|---|
| `/inventory-items` | Item CRUD + movements + reports |
| `/inventory/suppliers` | Supplier management + purchases |
| `/inventory/customers` | Customer management + sales |

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
| `/dashboard/summary` | Dashboard statistics |
| `/dashboard/alerts` | Active alerts |
| `/dashboard/charts` | Chart data (configurable date range) |
| `/reports/animals` | Animal reports |
| `/reports/medical` | Medical reports |
| `/reports/vaccination` | Vaccination reports |
| `/reports/employees` | Employee reports |

### Configuration & Admin

| Resource | Description |
|---|---|
| `/configuration/*` | Animal types, breeds, sex options, age categories, statuses, identification types, location types, locations, custom fields, farm config |
| `/audit-logs` | Audit log with entity/user/action/date filters |

## Roles

Seeded on startup: **SystemOwner**, **FarmManager**, **Veterinarian**, **Employee**, **Accountant**, **Viewer**

Some endpoints are role-restricted (e.g., breeding writes require Vet/FarmManager/SystemOwner).

## Deployment

Production API: `https://fms-api-3ba95327d590.herokuapp.com/api`

Deployed via GitHub Actions to Heroku container registry on every push to `main`. See `.github/workflows/deploy.yml`.
