# AGENTS.md — working notes for FMS

This file is for automated agents and contributors who need the conventions that are
not obvious from the code. `README.md` remains the user-facing overview; this file is the
short version of "how to build, test, and change this repo safely".

## Stack

- **Backend API:** .NET 8 / ASP.NET Core (`src/FMS.API`), Entity Framework Core, PostgreSQL.
- **Backend layers:** `src/FMS.Application` (interfaces, DTOs, `Result<T>`), `src/FMS.Domain`
  (entities, enums, zero dependencies), `src/FMS.Infrastructure` (EF Core, services, auth).
- **Solution:** `FarmManagementSystem.slnx` (Clean Architecture: API → Infrastructure →
  Application → Domain).
- **Frontend:** React 19 + TypeScript + Vite + Ant Design in `client/`.
- **Tests:** xUnit under `tests/FMS.Domain.Tests/`, Vitest under `client/`.
- **Mobile:** .NET MAUI Android wrapper in `src/FMS.Mobile` (build only, not needed for
  normal work).

## Build, test, lint, run

Run all of these from the repo root unless noted.

```bash
# Build the whole .NET solution
dotnet build FarmManagementSystem.slnx

# Backend tests — the whole project runs in one command; it takes ~1.5 minutes.
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj

# Optional faster split, if you only need one partition (each completes on its own):
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --filter "FullyQualifiedName!~FMS.Domain.Tests.E2E"
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --filter "FullyQualifiedName~FMS.Domain.Tests.E2E"

# Backend tests against a real PostgreSQL (optional; those tests SKIP without it).
# `Skipped: 0` is the pass condition only when FMS_TEST_POSTGRES is set.
# export FMS_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
# dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --nologo

# Frontend (from client/)
cd client
npx vitest run        # unit/component tests
npx tsc -b            # type check (no emit)
npm run lint          # oxlint + i18n hardcoded-string/locale-coverage/direction checks
npm run dev           # dev server on http://127.0.0.1:3000 (proxies /api to :5200)

# Run the API (needs PostgreSQL; migrations + role seeding run on startup)
dotnet run --project src/FMS.API     # http://localhost:5200, Swagger at /swagger in Development
```

The client's full check is `npx tsc -b && npx vite build` plus `npm run lint` and
`npx vitest run`.

## Demo Account feature

A shared, credential-free sandbox account that a visitor can explore. It is **not** a real
user: one flagged account (`User.IsDemo`) reached only through `POST /auth/demo-login`,
owning one farm that is restored to seed state by `POST /auth/demo-reset`.

### Demo files

- Seeder: `src/FMS.Infrastructure/Farm/DemoDataSeeder.cs`
- Config and guards: `src/FMS.Application/Common/DemoOptions.cs`,
  `src/FMS.Infrastructure/Auth/DemoGuards.cs`, `src/FMS.Infrastructure/Auth/AuthService.cs`
- Endpoints: `src/FMS.API/Controllers/AuthController.cs`
  (`POST /auth/demo-login`, `POST /auth/demo-reset`)
- Client: `client/src/api/demo.ts`, `client/src/stores/authStore.ts`,
  `client/src/components/DemoBanner.tsx`, `client/src/pages/LoginPage.tsx`,
  `client/src/components/AppLayout.tsx`
- Smoke tests: `tests/FMS.Domain.Tests/Demo/DemoSmokeTests.cs`
- Defaults: `Demo__Enabled=false` in `src/FMS.API/appsettings.json` and `.env.example`;
  `true` in `src/FMS.API/appsettings.Development.json`.

### Decisions (already made — do not re-litigate)

- The demo is **disabled by default**. With `Demo__Enabled=false`, both demo endpoints
  return **410 Gone** (not 404), so a deployment that never wanted a demo has no
  discoverable demo surface.
- The demo account is created **lazily on the first demo login**.
- `IsDemo` drives the client banner (`DemoBanner`) and the restricted-action guards.

## Rules

- **Do not change production UI code to make tests pass.** Fix the test, or the underlying
  behaviour, on its own merits.
- **Do not run database migrations, or write to production data, without asking.** A change
  that needs a migration is a decision, not a refactor.
- **Do not re-implement the Demo Account feature.** It is implemented and committed; only
  fix specific defects that are called out.
- Keep commits focused and separate by concern, and do not commit untracked junk (editor
  artifacts, `node_modules/.vitest*` caches, installers, ad-hoc report files).
- Guard every irreversible or sensitive action for the demo account via `DemoGuards`, and
  never change behaviour for non-demo users when adding such a guard.

## Known issues

- **The old parallel-run deadlock is fixed.** The E2E/`TestServer` suites each start an
  in-process `IHost`, and the two PostgreSQL fixtures used to probe for a server with
  `GetAwaiter().GetResult()` inside their constructors. Class-fixture constructors run on
  xUnit's bounded `MaxConcurrencySyncContext`, so blocking there while the probe's `await`s
  captured that same context deadlocked the run once enough fixtures blocked at once; the
  full run stalled with many host-based tests in flight. The probes now run via `Task.Run`,
  which clears the captured context, so collections run in parallel again and no
  `xunit.runner.json` override is needed. See `docs/test-hang-investigation.md`. The
  two-filter split above is optional.
- Tests that need real PostgreSQL or a real mailbox **skip** (never fail) when the
  environment variables above are unset.
