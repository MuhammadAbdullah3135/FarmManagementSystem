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
  `client/src/components/DemoBanner.tsx`, `client/src/pages/LoginPage.tsx`,
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

> Note: the demo feature itself already sits on the local `main` branch; this branch
> carries the follow-up fixes found while verifying it. The feature's own commits
> (`git log origin/main..main`) include `c5991ef` "Add the shared demo account, and fix
> what verifying it surfaced", `b296dd5` "Enforce the demo-account guards instead of
> leaving them as no-ops", `d4ca697` "Share and test the demo rate-limit policies", and
> `0b07d63` "Fix the DemoDataSeeder nullable warnings and drop the stray .env.example
> line".

## Verification performed

Environment: portable **PostgreSQL 16.11** (EnterpriseDB Windows binaries) on
`127.0.0.1:55432`, an empty throwaway database `fms`, the API on `127.0.0.1:5200` with
`Demo__Enabled=true` and `ASPNETCORE_ENVIRONMENT=Development`, and a disposable client
dev server. Nothing was run against a production database.

Backend (xUnit, `tests/FMS.Domain.Tests`):

| Run | Result |
| --- | --- |
| Full suite with `FMS_TEST_POSTGRES` set | **1119 passed, 3 skipped, 0 failed** (~1m37s) |
| PostgreSQL-gated subset (`~Postgres`) | **17 passed, 0 failed, 0 skipped** |
| Demo folder (`~FMS.Domain.Tests.Demo`) | **18 passed, 0 skipped, 0 failed** (`DemoGuardsTests` 12, `DemoSmokeTests` 4, `DemoEmailTransportTests` 2) |

The 3 skips are `RealEmailDeliveryTests` (they need a real mailbox); there are no
PostgreSQL-gated skips.

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
  InMemory test host; the 3 skipped tests need a real mailbox
  (`FMS_TEST_POSTGRES` / mailbox env). Not tested against another PostgreSQL version or
  production.
- **UI automation.** The walkthrough used a manual/harness browser session, not an
  automated Playwright suite; there is no automated regression test for `DemoBanner`.
- **Flaky frontend run.** A first full frontend run reported 11 failing tests across 4
  files while other work was running on the machine; an immediate clean re-run passed
  **804/804**. The failures did not reproduce and are unrelated to this change
  (`DemoBanner` is not rendered by any test).

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
