# FMS Client (React Frontend)

React single-page application for the Farm Management System. Provides the full dashboard and all management features, consumed by both browser users and the Android mobile wrapper.

## Tech

- **Framework:** React 19 + TypeScript 6
- **Build:** Vite 8
- **UI:** Ant Design 6 + Recharts (charts) + jsPDF/xlsx (exports)
- **State:** Zustand (auth + farm selection, persisted to localStorage)
- **Routing:** React Router 7
- **HTTP:** Axios with JWT interceptors (auto-refresh on 401)

## Running Locally

```bash
cd client
npm install
npm run dev
```

Opens at `http://127.0.0.1:3000`. The dev server proxies `/api` requests to `http://localhost:5200` (the .NET API).

### Scripts

| Command | Description |
|---|---|
| `npm run dev` | Start Vite dev server |
| `npm run build` | Type-check + production build |
| `npm run lint` | Run oxlint |
| `npm test` | Run Vitest component tests |
| `npm run preview` | Preview production build |

## Pages & Features

All routes are defined in `src/App.tsx`. Features verified against source code:

### Public Routes

| Path | Page | Description |
|---|---|---|
| `/login` | LoginPage | Email + password login |
| `/register` | RegisterPage | Account creation (name, email, password) |
| `/reset-password` | ResetPasswordPage | Email-based password reset |
| `/confirm-reset-password` | ConfirmResetPasswordPage | Completes a reset using the emailed token + new password |

### Protected Routes (requires login + farm selection)

| Path | Page |
|---|---|
| `/dashboard` | Dashboard — summary stats, alerts, charts (area/line/pie/bar via Recharts) |
| `/dashboard/notifications` | Notification centre — your alerts for the active farm, with read/dismiss state and a "View" link to the screen that fixes each one. The header bell badges the unread count |
| `/dashboard/notifications/preferences` | Per alert type: in-app and email channels |

#### Admin
| Path | Page |
|---|---|
| `/dashboard/admin/audit-log` | Audit log (system owner / farm manager / accountant) |
| `/dashboard/admin/jobs` | Scheduled job status: recurring jobs, last run/state/error, recent failures (SystemOwner only) |

> The job-status page is account-scoped (its endpoint is `/api/admin/jobs`, with no farm in the path), so unlike every other protected page it renders without a farm selected. The notification pages are farm-scoped, like every other protected page, because notifications belong to a farm membership. The header bell shows the *active* farm's unread count and refreshes on navigation.

#### Feed Management
| Path | Page |
|---|---|
| `/dashboard/feed/types` | Feed type CRUD (Forage, Concentrate, Mineral, Supplement, Additive, Other) |
| `/dashboard/feed/records` | Per-animal or per-location feed records |
| `/dashboard/feed/diet-plans` | Diet plans with itemized compositions |
| `/dashboard/feed/schedules` | Time-based feeding schedules |
| `/dashboard/feed/tasks` | Feeding task generation, completion, skip |
| `/dashboard/feed/reports` | Consumption trends, by type/animal/location, cost summary |

#### Animals
| Path | Page |
|---|---|
| `/dashboard/animals` | Animal list with search/filter (type, breed, status, location) |
| `/dashboard/animals/import` | Bulk import wizard: upload CSV/Excel → map columns → per-row validation preview → all-or-nothing commit |
| `/dashboard/animals/:id` | Animal detail (weights, timeline, identifications) |

#### Breeding
| Path | Page |
|---|---|
| `/dashboard/breeding/records` | Breeding record CRUD |
| `/dashboard/breeding/gestation` | Gestation tracking + confirm/revert + health checks |
| `/dashboard/breeding/births` | Birth recording with dynamic offspring |
| `/dashboard/breeding/lineage` | Ancestor/descendant tree |
| `/dashboard/breeding/reports` | Breeding summary, trend, methods, sires, calendar |

#### HR
| Path | Page |
|---|---|
| `/dashboard/hr/employees` | Employee management + salary payments |
| `/dashboard/hr/departments-roles` | Department and role management |
| `/dashboard/hr/salary-payments` | Payroll report |
| `/dashboard/hr/attendance` | Check-in/check-out + attendance records |
| `/dashboard/hr/performance` | Performance reviews |

#### Finance
| Path | Page |
|---|---|
| `/dashboard/finance/expenses` | Expense tracking (ProTable) |
| `/dashboard/finance/incomes` | Income tracking (ProTable) |
| `/dashboard/finance/reports` | Profit/loss, breakdowns, monthly summary |
| `/dashboard/finance/categories` | Expense/income categories + payment methods |

#### Health
| Path | Page |
|---|---|
| `/dashboard/health/medical` | Medical records |
| `/dashboard/health/medicines` | Medicine inventory + stock batches |
| `/dashboard/health/medicines/alerts` | Expiry and low-stock alerts |
| `/dashboard/health/vaccines` | Vaccine type management |
| `/dashboard/health/vaccinations` | Vaccination records |
| `/dashboard/health/vaccinations/schedule` | Vaccination schedules + overdue |
| `/dashboard/health/weight-schedules` | Weight check schedules |
| `/dashboard/health/costs` | Health cost reports |

#### Inventory
| Path | Page |
|---|---|
| `/dashboard/inventory/items` | Inventory items + stock movements |
| `/dashboard/inventory/movements` | Stock movement history |
| `/dashboard/inventory/suppliers` | Supplier management + purchases |
| `/dashboard/inventory/customers` | Customer management + sales |
| `/dashboard/inventory/reports` | Inventory reports |

#### Tasks
| Path | Page |
|---|---|
| `/dashboard/tasks` | Farm tasks with state machine (Pending -> In Progress -> Completed/Cancelled -> Reopen) |

#### Reports
| Path | Page |
|---|---|
| `/dashboard/reports/animals` | Animal reports |
| `/dashboard/reports/financial` | Financial reports |
| `/dashboard/reports/feed` | Feed reports |
| `/dashboard/reports/breeding` | Breeding reports |
| `/dashboard/reports/medical` | Medical reports |
| `/dashboard/reports/vaccination` | Vaccination reports |
| `/dashboard/reports/employees` | Employee reports |

#### Admin
| Path | Page |
|---|---|
| `/dashboard/admin/audit-log` | Audit log (entity type, user, action, date range filters) |

#### Configuration
| Path | Page |
|---|---|
| `/dashboard/configuration` | Farm configuration — animal types, breeds, statuses, locations, custom fields, farm config |

## Key UI Features

- **Farm switcher** in the header — all data is scoped to the selected farm via `X-Farm-Id` header
- **Responsive sidebar** — collapses to a hamburger drawer on narrow screens
- **Export** — PDF, Excel, CSV from any table (via `ExportButton` component)
- **Date range filtering** — reusable `DateRangeFilter` component (default: last 30 days)
- **Charts** — Recharts area/line/pie/bar on dashboard and report pages
- **Error boundary** — catches render errors and shows a reload button (built for Android WebView crash resilience)
- **Auto-refresh** — on 401, silently refreshes the JWT and replays the failed request
- **Offline app shell** — a service worker precaches the built app, so the Android wrapper opens without a connection instead of showing a blank screen. A banner states what the device has stored and how stale it is. Reads and writes still need the connection: see the first limitation below.
- **Role-filtered menu** — sidebar items are filtered per farm role (`usePermissions` reads the active farm's `userFarmRole`), so switching farms recomputes what is shown

## Authentication

- JWT access tokens (15 min) + refresh tokens (30 days) stored in localStorage
- Auto-refresh interceptor: on 401, silently refreshes and replays the request
- On refresh failure: clears all auth state and redirects to `/login`

## Known Limitations

- **No i18n:** English only. Ant Design locale is hardcoded to `en_US`.
- **No farm management UI:** The header can switch between farms, and `farmStore` exposes a `createFarm` action, but no page lets users create, rename, or delete a farm. The backend `FarmsController` supports full farm CRUD.
- **Offline is provisional:** the app shell loads offline and the device keeps a per-account, per-farm IndexedDB store (`src/offline/`), but nothing reads from it yet and no writes are queued — pages still fetch, and an offline session cannot record a weight, an attendance or a task completion. Cached reads, the write queue and the sync endpoint are the next increments; see [docs/OFFLINE.md](../docs/OFFLINE.md) for the approved design and what each one adds.
- **Single types file:** All domain types are defined in a single `src/types/index.ts` file (1000+ lines).

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `VITE_API_URL` | `/api` | Backend API base URL. Production: `https://fms-api-3ba95327d590.herokuapp.com/api` |

## Deployment

Automated via GitHub Actions on every push to `main` (see `.github/workflows/pages-deploy.yml`). The build copies `index.html` to `404.html` for SPA client-side routing fallback.

Live at: `https://muhammadabdullah3135.github.io/FarmManagementSystem/`
