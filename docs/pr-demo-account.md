# Demo account (`feature/demo-account`)

## Summary

A shared, credential-free sandbox account a visitor can explore. It is **not** a real
user: one flagged account (`User.IsDemo`) reached only through
`POST /api/auth/demo-login`, owning one farm (`Sunrise Ranch`) that is restored to seed
state by `POST /api/auth/demo-reset`.

- **Disabled by default.** With `Demo__Enabled=false` (the default in
  `appsettings.json` and `.env.example`), both demo endpoints return **410 Gone**, so a
  deployment that never wanted a demo has no discoverable demo surface. Development
  overrides it to `true`.
- **Lazily created** on the first demo login (`DemoDataSeeder`), so no demo rows exist
  until someone asks for them.
- **Farm-isolated.** The demo user owns only the demo farm; a demo session's writes are
  confined to it, and `demo-reset` restores the farm and its sample data.
- **Restricted actions** are refused with a localisable `validation.demo.*` key:
  password reset/change, real email, real payments (salary ledger), bulk export, and
  (for the future) account deletion. See `DemoGuards` and
  `EmailService.IsDemoRecipient`.
- **Client:** a persistent `DemoBanner`, a "Try the demo" button on `LoginPage`, and
  translations for `en` / `es` / `ar` (RTL-aware) for the banner and the blocked-action
  messages.

### Demo files

- Seeder: `src/FMS.Infrastructure/Farm/DemoDataSeeder.cs`
- Guards: `src/FMS.Infrastructure/Auth/DemoGuards.cs`,
  `src/FMS.Infrastructure/Auth/EmailService.cs`,
  `src/FMS.Infrastructure/Auth/AuthService.cs`
- Endpoints: `src/FMS.API/Controllers/AuthController.cs`
- Client: `client/src/api/demo.ts`, `client/src/stores/authStore.ts`,
  `client/src/pages/LoginPage.tsx`, `client/src/components/DemoBanner.tsx`
  (rendered by `client/src/components/AppLayout.tsx`),
  `client/src/i18n/locales/{en,es,ar}/demo.json`
- Tests: `tests/FMS.Domain.Tests/Demo/` (`DemoSmokeTests`, `DemoGuardsTests`,
  `DemoEmailTransportTests`)

## Commits on this branch

`git log main..feature/demo-account`:

| Commit | Date | Subject |
| --- | --- | --- |
| `30fefb4` | 2026-10-17 | Add the missing migration for `User.IsDemo` |
| `c33d26f` | 2026-10-17 | Use antd v6 prop names in the demo banner |
| `769ef4b` | 2026-10-17 | Add the demo-account pull-request description |
| `ec229dd` | 2026-10-17 | Test that demo recipient emails never reach the transport |
| `16f3ab3` | 2026-10-17 | Update the PR description with final commits and test coverage |

> The table tracks `git log main..feature/demo-account`. A later commit that only edits
> this document (the correction carrying this note) is not itemized, because a document
> cannot list the commit that contains it.
>
> Note: the demo feature itself already sits on the local `main` branch; this branch
> carries the follow-up fixes found while verifying it. The demo-related commits in
> `git log origin/main..main` (that range also carries unrelated test-hang and i18n work)
> include `c5991ef` "Add the shared demo account, and fix what verifying it surfaced",
> `b296dd5` "Enforce the demo-account guards instead of leaving them as no-ops",
> `d4ca697` "Share and test the demo rate-limit policies", `0b07d63` "Fix the
> DemoDataSeeder nullable warnings and drop the stray .env.example line", and `b94c4a1`
> "Add AGENTS.md with build/test conventions and the demo-account decisions".

## Verification performed

Environment: portable **PostgreSQL 16.11** (EnterpriseDB Windows binaries) on
`127.0.0.1:55432`, an empty throwaway database `fms`, the API on `127.0.0.1:5200` with
`Demo__Enabled=true` and `ASPNETCORE_ENVIRONMENT=Development`, and a disposable client
dev server. Nothing was run against a production database.

Backend (xUnit, `tests/FMS.Domain.Tests`):

| Run | Result |
| --- | --- |
| Full suite, `FMS_TEST_POSTGRES` **unset** (this pre-submission check) | **1107 passed, 17 skipped, 0 failed** (2m20s) |
| Full suite, `FMS_TEST_POSTGRES` **set** (measured before `ec229dd` added 2 tests) | **1119 passed, 3 skipped, 0 failed** (~1m37s); a re-run would add those 2 tests (1121) |
| `~Postgres` filter, `FMS_TEST_POSTGRES` **set** | **17 passed, 0 failed, 0 skipped** (the filter also matches 3 tests that do not need PostgreSQL) |
| Demo folder (`~FMS.Domain.Tests.Demo`) | **18 passed, 0 skipped, 0 failed** (`DemoGuardsTests` 12, `DemoSmokeTests` 4, `DemoEmailTransportTests` 2) |

With `FMS_TEST_POSTGRES` unset, 17 tests skip: the 14 PostgreSQL-gated integration tests
(`DashboardPostgresIntegrationTests` 7, `HangfirePostgresIntegrationTests` 3,
`IdempotencyIndexPostgresIntegrationTests` 2, `FarmExportLargeFarmPostgresTests` 1,
`ReportQueryCountPostgresTests` 1) plus the 3 `RealEmailDeliveryTests` (they need a real
mailbox). With the variable set, only the 3 mailbox tests skip. No test that runs fails.

Demo flow end to end:

- `POST /api/auth/demo-login` → **200**, `isDemo:true`, `roles:["SystemOwner"]`, farm
  `Sunrise Ranch`; `UserFarms.Role=SystemOwner`.
- `POST /api/auth/demo-reset` → **200** `{"demoReset":true}`; a tampered farm name was
  restored (`Tampered Ranch` → `Sunrise Ranch`).
- With `Demo__Enabled=false`: both endpoints **410**
  `"The demo feature is not enabled on this server."`; an unauthenticated reset is
  **401**.
- Browser walkthrough: credential-free login, the banner, and the three blocked actions
  (export, salary payment, password reset) each show the friendly message driven by the
  response `x-message-key`; `en` / `es` / `ar` render translated with no raw keys
  (`es` sets `lang=es`; `ar` sets `lang=ar dir=rtl`). 9 screenshots.

Guard round (this branch):

- **Migration defect.** `POST /api/auth/demo-login` first returned
  `500 42703: column u.IsDemo does not exist` against PostgreSQL: `User.IsDemo` existed
  in the entity but had no migration (InMemory tests cannot catch this). Fixed in
  `30fefb4` (`Users.IsDemo boolean NOT NULL DEFAULT false`).
- **Email guard** (`demoEmailSendingDisabled` / `EmailService.IsDemoRecipient`):
  - As the demo user on the demo farm, `POST /api/farm/{farmId}/invitations` for
    `demo@farm.local` → **409** `validation.conflict.alreadyMember` (the demo user is a
    member of every farm they administer, so the invite never reaches the send).
  - As a real user on their own farm, the same invite → **200**, and **no**
    `x-message-key` (the guard is a silent no-op by design).
  - The transport skip itself is now pinned by `DemoEmailTransportTests`: a send to the
    demo address leaves a recording transport untouched, while the same three sends to a
    normal address reach it (the control).
- **Delete-account guard** (`demoDeleteAccountDisabled`): no account-deletion endpoint
  exists in this build, so it could not be exercised at the endpoint level.

## Known gaps

- **Delete-account guard has no endpoint.** `DemoGuards.DemoDeleteAccountBlocked()` has no
  production caller and there is no account-deletion endpoint anywhere in the API, so the
  guard cannot be exercised. None was invented. (No account-deletion capability is
  expected in this build.)
- **Email guard is now covered; two message keys remain unreachable from an endpoint.**
  `DemoEmailTransportTests` pins that a send to the demo address never reaches the
  transport, and `DemoGuardsTests` asserts the guard's *shape* (the
  `validation.demo.demoEmailSendingDisabled` key). But `DemoGuards.DemoEmailSendingBlocked()`
  and `DemoGuards.DemoDeleteAccountBlocked()` have no production caller: the email
  suppression is silent (no key is emitted) and there is no deletion endpoint. Both keys
  exist only for a future wire-up.
- **Application logging is not wired (pre-existing, unrelated).**
  `src/FMS.API/Program.cs:111` calls `builder.Host.UseSerilog()` without
  `ReadFrom.Configuration(...)`, and there is no other `LoggerConfiguration`/`Log.Logger`.
  The Serilog sinks configured in `appsettings.json` (Console + rolling File) are
  therefore inert and the app emits no logs. This pre-dates the demo feature and is not
  touched here, but it means the "email to the demo address was not delivered" Debug line
  could not be captured as evidence.
- **Environment coverage.** Verified only against a throwaway PostgreSQL 16.11 and an
  InMemory test host. Without `FMS_TEST_POSTGRES`, the 14 PostgreSQL-gated tests skip;
  the 3 `RealEmailDeliveryTests` skip unless `FMS_TEST_EMAIL` (plus
  `FMS_TEST_EMAIL_PROVIDER`, `FMS_TEST_SENDGRID_API_KEY`, `FMS_TEST_EMAIL_FROM`) is set.
  Not tested against another PostgreSQL version or production.
- **UI automation.** The walkthrough used a manual/harness browser session, not an
  automated Playwright suite; there is no automated regression test for `DemoBanner`.
- **Flaky frontend run.** A first full frontend run reported 11 failing tests across 4
  files while other work was running on the machine; an immediate clean re-run passed
  **804/804**. It reproduced during this check: with the backend suites running
  concurrently, 5 tests across 3 files timed out at 5s; each passed on an isolated
  re-run and an uncontended full run passed **804/804** (85 files). The failures are
  unrelated to this change (`DemoBanner` is not rendered by any test).

## Enabling demo mode

Server:

1. Set `Demo__Enabled=true` (Development already does; `appsettings.json` and
   `.env.example` ship `false`).
2. Optionally set `Demo__DemoEmail` (defaults to `demo@farm.local`).
3. Apply migrations (`20261017165739_AddUserIsDemo` is the demo-related one) — the API
   runs them on startup in Development.
4. Endpoints: `POST /api/auth/demo-login` (`AllowAnonymous`) and
   `POST /api/auth/demo-reset` (`[Authorize(Roles="SystemOwner")]`). When disabled, both
   return **410 Gone**.

Client:

- No configuration is required. `LoginPage` shows the "Try the demo" button; the banner
  renders whenever the authenticated user has `isDemo=true`. `VITE_API_URL` must point
  at the API (the client's `.env.local`/`.env.production` point at the deployed API).
