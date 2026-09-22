# Environment verification — status, evidence and runbooks

Phase 1–3 delivered a lot of work that was verified **by code inspection and on the
EF InMemory provider only**. This file tracks the four items that could not be
proven in the development environment, states honestly which have since been
verified, and hands the rest over as runnable commands.

The development environment used for this work has **no Docker**, **no PostgreSQL
server or `psql` client**, and therefore no way to run the container stack or the
provider-specific test suite. It *does* have an authenticated `gh` CLI, so the CI
item could be verified for real.

| # | Item | Status | Basis |
|---|------|--------|-------|
| 1 | PostgreSQL integration suite (dashboard + Hangfire storage) | **HANDED OFF** | No server, no client, no Docker in this environment |
| 2 | CI gate: a failing suite blocks the deploy job | **DONE** | Five real GitHub Actions runs, URLs below |
| 3 | `docker compose` stack + its fail-fast guards | **HANDED OFF** | Docker not installed; compose corrected first so the runbook can pass |
| 4 | Rotate the two leaked secrets | **HANDED OFF (account owner)** | Requires production credentials; guard + tripwires landed here |
| 5 | Live email delivery (reset, invitation, digest) | **HANDED OFF** | No provider credentials or mailbox in this environment |
| 6 | Offline shell inside a real Android WebView | **HANDED OFF** | No device or emulator here; jsdom cannot exercise the WebView's cache or a service worker's install |

---

## 1. PostgreSQL integration suite — HANDED OFF

### What is skipped today

| Test | File | Why skipped |
|---|---|---|
| `Summary_OnRealPostgres_ComputesUpcomingBirths` | `tests/FMS.Domain.Tests/E2E/DashboardPostgresIntegrationTests.cs` | No PostgreSQL reachable on `FMS_TEST_POSTGRES` |
| `Alerts_OnRealPostgres_IncludesDueBirthAlert` | same | same |
| `Initialize_OnRealPostgres_InstallsTheHangfireSchema` | `tests/FMS.Domain.Tests/Jobs/HangfirePostgresIntegrationTests.cs` | same (added in this subphase) |
| `Initialize_OnRealPostgres_RegistersTheThreeRecurringJobs` | same | same |
| `Initialize_OnRealPostgres_IsIdempotentAcrossRestarts` | same | same |

The three new ones close a gap the scheduler's own tests name explicitly:
`BackgroundJobsSetupTests` can only reach the container wiring, because "the job
store is PostgreSQL, which the InMemory test provider cannot stand in for". The
Hangfire schema name, the schema installation, the recurring-job writes and their
idempotency across restarts were therefore never exercised anywhere — and a mistake
in any of them (`SchemaName`, `PrepareSchemaIfNecessary`, a colliding job id) would
first appear in production.

### Runbook

Prerequisites: a **writable** PostgreSQL 14+ server. The tests create and drop
throwaway databases (`CREATE DATABASE fms_test_<guid>`, then
`DROP DATABASE … WITH (FORCE)`), so the role needs `CREATEDB`.

> Neon: a standard Neon role does **not** have `CREATEDB`, so the suite will skip
> there. Either grant `CREATEDB` to the role, or (preferred) point this at a local
> server. Production connection details are not usable for this.

```bash
# Any of these provide a local server:
#   docker run --rm -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
#   a locally installed PostgreSQL, or a Neon database whose role owns CREATEDB

export FMS_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"

# The Postgres-gated tests, alone. "Skipped: 0" is the pass condition.
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj \
  --filter "FullyQualifiedName~PostgresIntegrationTests" \
  --logger "console;verbosity=normal"

# Then the whole suite against a real provider.
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj --nologo
```

Expected: `Failed: 0` and **`Skipped: 0`**. Before this run the same command reports
`Skipped: 5`; a run that still reports skips has not connected to anything and
proves nothing.

### What to report back

- The final `Passed!/Failed!` summary line for both commands, verbatim.
- For the Hangfire tests specifically: whether the `hangfire` schema and its
  `job`/`state`/`set` tables were found, and whether all three recurring jobs came
  back with their crons (`5 0 * * *`, `0 * * * *`, `*/15 * * * *`).
- If either new Hangfire test fails, the assertion text — that is new signal about
  the storage path, and worth fixing before the next release.

---

## 2. CI gate — DONE

### What was wrong

Neither workflow had a `pull_request` trigger. `deploy.yml` and `pages-deploy.yml`
ran only on `push: [main]` (or manual dispatch), so **a pull request ran no CI at
all** — the only way to reach a test run was to push to `main`, which is the release
path itself. The `needs: [test-backend, test-client]` gate on the deploy job was
real, but nothing guarded the branch.

### The change

Both workflows now also trigger on `pull_request: [main]`, and each deploy job is
guarded with:

```yaml
if: github.event_name != 'pull_request' && github.ref == 'refs/heads/main'
```

So a PR runs the suites and can never publish, and a manual dispatch on a scratch
ref is safe too. **This only takes effect once it reaches `main`** — for
`pull_request` events GitHub runs the workflow file from the *base* branch.

### Evidence — five real runs, same scratch branch

Branch `verify/ci-gate-probe` (throwaway, deleted afterwards) held the workflow
change plus two deliberately failing tests,
`FMS.Domain.Tests.CiGateProbeTests.DeliberateFailure_ForCiGateProbe` and
`src/CiGateProbe.test.ts`. Commits: `2b6bb9c` (workflow gate), `3973b4e` (deliberate
failures), then the revert.

| Run | Workflow | Result |
|---|---|---|
| [35572879383](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/actions/runs/35572879383) | deploy.yml | `test-backend` **failure**, `test-client` **failure**, `deploy` **skipped** |
| [35572881580](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/actions/runs/35572881580) | pages-deploy.yml | `build` **failure**, `deploy` **skipped** |
| [35573070857](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/actions/runs/35573070857) | deploy.yml | `test-backend` **success**, `test-client` **success**, `deploy` **skipped** |
| [35573072965](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/actions/runs/35573072965) | pages-deploy.yml | `build` failure (unrelated flake, see below), `deploy` **skipped** |
| [35573260118](https://github.com/MuhammadAbdullah3135/FarmManagementSystem/actions/runs/35573260118) | pages-deploy.yml | `build` **success**, `deploy` **skipped** |

The failure that produced the red run, quoted from the job log:

```
test-backend  Failed FMS.Domain.Tests.CiGateProbeTests.DeliberateFailure_ForCiGateProbe
test-backend     Deliberate failure: this branch exists to prove the CI gate, not to pass.
test-client   FAIL  src/CiGateProbe.test.ts > CI gate probe > deliberately fails
```

`deploy` is skipped in the green runs because the ref is not `main` — the guard
working, not merely the `needs:` gate.

### Finding: an intermittent failure in the client suite on a runner

Runs `35573072965` and `35573260118` are the **same commit**. One failed, one
passed, so this is a flake, not a regression from this subphase:

```
Vitest caught 1 unhandled error during the test run.
ReferenceError: window is not defined
This error originated in "src/pages/configuration/ConfigurationPage.test.tsx"
```

Vitest fails the run on an unhandled error even when every test passes. Local runs
(Windows, Node 24) never reproduce it; the runner (Ubuntu, Node 22) does, sometimes.
It is latent on `main` — every `main` run before this date happened to pass. Worth
tracking down separately: an async task outliving its jsdom environment.

### Reproducing the gate evidence

```bash
git switch -c verify/ci-gate-probe
# add a failing test, commit it (paths only — never stage unrelated work), push
gh workflow run deploy.yml      --ref verify/ci-gate-probe
gh workflow run pages-deploy.yml --ref verify/ci-gate-probe
gh run list --branch verify/ci-gate-probe
gh run view <run-id> --json jobs -q '.jobs[] | "\(.name) -> \(.status)/\(.conclusion)"'
# then remove the failing test, push, dispatch again for the green run
```

---

## 3. docker-compose stack — HANDED OFF

### What was wrong before handing over

The stack could not work: `docker-compose.yml` ran `mcr.microsoft.com/mssql/server:2022`
and gave the API
`ConnectionStrings__DefaultConnection=Server=db;…;User=sa;…;TrustServerCertificate=true`,
while the application is Npgsql. Npgsql accepts `Username` / `User Id` / `UID` but
**not** `User`, so the API container would have failed on connection-string parsing
even with both containers healthy. (`appsettings.json`'s default connection string
had the same problem, as `Trusted_Connection`.) This drifted in Phase 1.7, when the
application moved to PostgreSQL and compose did not follow.

`TrackedConfigDriftTests` now asserts compose and `appsettings.json` stay shaped for
Npgsql, so the two cannot silently diverge again.

**What is *not* verified here:** the corrected compose file has never been parsed by
a YAML parser. This environment has no Python and no YAML library, so the syntax is
inspection-only — start the runbook below with `docker compose config`, which parses
the file and prints the resolved configuration, and report a failure there if there
is one rather than assuming the edit is valid.

### Runbook

```bash
cp .env.example .env
# DB_PASSWORD=<strong value, no semicolons>
# JWT_SIGNING_KEY=$(openssl rand -base64 48)

docker compose up --build -d
docker compose ps                    # api and db both report "healthy"
docker compose logs api | tail -30    # EF migrations, Hangfire schema, "Now listening on"
curl -i localhost:8080/health/ready   # 200 {"status":"Healthy"}
```

Expected in `docker compose ps`: **two** services, both `healthy`. The `db` service
uses a `pg_isready` healthcheck, and the `api` service will not start until it
passes (`condition: service_healthy`).

```bash
# 3a. The fail-fast guard: an empty DB_PASSWORD must abort, not default
printf 'JWT_SIGNING_KEY=%s\n' "$(openssl rand -base64 48)" > .env   # DB_PASSWORD deliberately absent
docker compose up            # expect: "ERROR: DB_PASSWORD is not set..." and a non-zero exit

# 3b. Bonus: the API's own signing-key guard is active in this stack
#     (ASPNETCORE_ENVIRONMENT=Production), so a placeholder key must abort too
printf 'DB_PASSWORD=%s\nJWT_SIGNING_KEY=%s\n' "SomeStrongPassword1" \
  "DEV-ONLY-INSECURE-JWT-SIGNING-KEY-NOT-FOR-ANY-OTHER-ENVIRONMENT" > .env
docker compose up            # expect: "Refusing to start ... placeholder"

docker compose down -v       # -v removes the pgdata volume
```

### What to report back

- `docker compose ps` output (both services' health).
- Whether the `api` log shows EF migrations applied and the Hangfire schema
  installed.
- The exact output of both abort tests (3a and 3b), including the exit codes.
- Whether `/health/ready` returned 200.

---

## 4. Secret rotation — HANDED OFF (account owner)

### Current status, re-verified in this repository

| Secret | Value | In tracked files today? | In git history? |
|---|---|---|---|
| SQL Server SA password | `YourStrong!Password123` | **No** — only `${DB_PASSWORD}` in compose | **Yes** — added in `0d06b20`, removed in `23c2efc` |
| JWT signing key | `YourSuperSecretKeyThatIsAtLeast32BytesLong!ChangeThisInProduction!` | **Was** — `appsettings.json`; **now removed** | Yes — also in `.env.example` before Phase 1 |

Neither is a production credential in this repository: production is Heroku +
PostgreSQL (Neon), and the SA password only ever applied to the local SQL Server
container that compose no longer runs. Treat them as compromised anyway, because
anyone with the history can read them and nothing proves they were never reused.

### The guard that stops rotation from being a no-op

`src/FMS.API/Security/JwtSigningKeyGuard.cs`, called before any service is
registered, refuses to start outside Development when `Jwt:SecretKey` is missing,
shorter than 32 bytes, or still the committed placeholder. Before it, a production
deploy that forgot `Jwt__SecretKey` silently signed every token — including refresh
tokens — with a key published in this repository, and a JWT signed with a public key
is not a weak credential, it is no credential.

Verified by running the real entry point, not just unit tests:

| Case | Result |
|---|---|
| `Production`, key unset | `InvalidOperationException: Refusing to start in the 'Production' environment. Jwt:SecretKey is not configured.` — never listened |
| `Production`, placeholder key | `… Jwt:SecretKey is still the development placeholder committed to this repository. Anyone with the repository can forge a token, including a SystemOwner one.` — never listened |
| `Production`, 31-byte key | `… Jwt:SecretKey must be at least 32 bytes long for HS256; the configured value is 31 bytes.` — never listened |
| `Development`, key unset | **0 refusals** — the committed dev key applies and the app proceeds to EF migrations |

### Runbook — order matters

> **Do this first.** Confirm production already has a real `Jwt__SecretKey` before
> the guard is deployed, or the next release will refuse to start. Check with
> `heroku config:get Jwt__SecretKey --app <app>` (or the Heroku dashboard). If it is
> empty, set it as part of step 1, *before* merging the guard.

```bash
# 1. JWT signing key
openssl rand -base64 48                      # generate
heroku config:set Jwt__SecretKey="<new>" --app <app>
heroku ps:restart --app <app>                # or just deploy

# 2. Database password (Neon)
#    a. Rotate the role's password in the Neon console.
#    b. Immediately update the connection string, so the window is as short as possible:
heroku config:set ConnectionStrings__DefaultConnection="Host=<neon-host>;Database=<db>;Username=<user>;Password=<new>;Ssl Mode=Require" --app <app>
heroku ps:restart --app <app>
```

Consequences, stated so they are not a surprise:

- **Rotating the JWT key signs every user out.** Every issued access *and* refresh
  token becomes invalid at once; users must log in again. That is the point, but it
  is a user-visible event — pick a quiet moment.
- The database password must be updated in the same window as the rotation, or the
  API cannot connect in between.
- The SA password needs **no** production action unless it was reused somewhere
  outside this repository. Confirm it is not any live credential; the local
  `docker compose` password is now `DB_PASSWORD` from an untracked `.env`.
- Removing the values from history requires rewriting history and force-pushing,
  which every existing clone keeps a copy of. Not recommended here; rotation is what
  makes the old values worthless, and that is steps 1 and 2.

### What to report back

- Confirmation that `Jwt__SecretKey` and the database connection string were
  rotated, with the date.
- The `heroku config:get Jwt__SecretKey --app <app>` result *before* the guard ships
  (present/length only — never paste the value).
- Whether the sign-out was expected and acceptable.

---

## 5. Live email delivery — HANDED OFF

### What is verified here, and what is not

The transport, the rendering, the retry policy, the guard and the caller behaviour are
all covered by tests that run in this environment (89 in total: 86 pass, 3 skip — the
`tests/FMS.Domain.Tests/Email/**` files plus `EmailConfigurationDriftTests`). What no amount
of those can prove is **deliverability**: that a real
provider accepts these messages and a real mailbox receives them. That needs sender
verification and a mailbox, neither of which exists here, so three tests **SKIP** with a
runbook as their skip message rather than pretending to pass:

| Test | Flow |
|---|---|
| `RealEmailDeliveryTests.PasswordReset_IsDeliveredToARealInbox` | password reset |
| `RealEmailDeliveryTests.FarmInvitation_IsDeliveredToARealInbox` | farm invitation |
| `RealEmailDeliveryTests.AlertDigest_IsDeliveredToARealInbox` | alert digest (two alerts, one Critical, one Warning) |

### Preconditions — these block a live send, not optional

Both are owner actions. Enabling `Email:Provider=SendGrid` with either still in place sends
**real** emails to **real** inboxes.

1. **`Frontend:BaseUrl` must be the deployed frontend URL.**
   `appsettings.Production.json` still ships the template value `https://yourdomain.com`.
   Every email contains a link built from this setting, so with it in place the recipient
   gets a dead link in a message that looks legitimate. The guard warns about exactly this
   at startup (`Email configuration: Frontend:BaseUrl is still the template value…`), and
   `EmailConfigurationDriftTests` asserts the warning and the value stay consistent.
   Set `Frontend__BaseUrl` (Heroku config var) to the real URL, **including any base path**
   — the Pages deployment is served from a repo subpath, so the trailing path matters.
2. **`Cors:AllowedOrigins` must match that URL.** Not an email setting, but the same
   consequence: `appsettings.Production.json` still lists `https://yourdomain.com`, so the
   deployed site's own API calls would be rejected until it matches. Set
   `Cors__AllowedOrigins__0` to the deployed frontend origin.

Both are asserted as still-unfixed today by `EmailConfigurationDriftTests`
(`AppSettings_Production_StillNeedsItsFrontendUrlSetBeforeDeliveryIsEnabled`,
`AppSettings_Production_CorsOriginIsStillTheTemplateValue`), so they cannot be forgotten
silently — those two tests start failing the moment the values are corrected, at which
point delete them.

### Runbook

```bash
# 1. Verify a sender (no domain needed): SendGrid -> Settings -> Sender Authentication
#    -> Single Sender Verification. Then create an API key with the "Mail Send"
#    permission only. Neither value belongs in a file that is committed.
export FMS_TEST_EMAIL="a-mailbox-you-can-open@example.com"
export FMS_TEST_EMAIL_PROVIDER=SendGrid
export FMS_TEST_EMAIL_FROM="the-address-you-verified@example.com"
export FMS_TEST_SENDGRID_API_KEY="SG.…"
# Optional: what the links should point at. Defaults to https://example.com.
export FMS_TEST_FRONTEND_URL="https://your-frontend.example.com"

# 2. Send all three flows (one message each) and print what was sent.
cd tests/FMS.Domain.Tests
dotnet test FMS.Domain.Tests.csproj \
  --filter FullyQualifiedName~RealEmailDeliveryTests --logger "console;verbosity=detailed"
```

Each test prints a `[real-email]` line with the recipient, subject and the extracted links
before it asserts, so the run's own output is the evidence. `Skipped: 0` and `Passed: 3`
is the pass condition — anything else means the properties above were not supplied.

### What to report back

- The three subjects that arrived (or, for each one: `Passed` with the message in the
  inbox, or the exact provider rejection text).
- Whether the **reset** and **invitation** links opened the frontend (not just resolved) —
  that is what proves `Frontend:BaseUrl` is production-ready.
- Whether the digest rendered in a real mail client: both alert rows, their severity
  colours, and its two links (notification centre and preferences).
- Whether the message reached the **inbox** rather than the spam folder, and the spam
  score if the client shows one. A first message from an unverified-domain sender often
  does land in spam; that is a sending-domain problem, not a code problem, and it is worth
  knowing before delivery is switched on for real users.
- Confirmation that the API key was **not** written to the application log during the run.

### Then, to enable it for real

```bash
heroku config:set Email__Provider=SendGrid \
  Email__FromAddress="the-address-you-verified@example.com" \
  Email__SendGrid__ApiKey="SG.…" \
  Frontend__BaseUrl="https://your-frontend.example.com" \
  Cors__AllowedOrigins__0="https://your-frontend.example.com" --app <app>
# Failure modes are now loud rather than silent: a missing key refuses to start rather
# than dropping every reset, and a missing/template frontend URL logs a warning at boot.
```

---

## 6. Offline shell in a real Android WebView — HANDED OFF

### What is verified here, and what is not

Verified in this repository, by the client suite (`cd client && npm test`):

- `vite build` emits `sw.js` with a precache manifest read from the **written output
directory**, so the worker can only ever ask for files that build produced — and the build
fails loudly when the app shell is missing or the manifest would be empty (this guard caught
its own first implementation, which read the in-memory bundle and found no `index.html`).
- Registration is production-only, silent when it fails, and never interrupts the user with a
reload.
- The store's schema, its per-version migration step, its `(accountId, farmId, …)` scoping and
  every clearing path behave correctly against a real IndexedDB implementation
  (`fake-indexeddb` supplied to jsdom, which has none).
- Transaction scope is correct: a store not listed on the transaction throws, which is how a
  silent cross-store write failure was found in this increment.

**Not verified, and therefore not claimed:** anything that depends on the Android WebView
itself. jsdom has no service worker and no HTTP cache, so the three checks below can only be
run on a device or emulator. They are the reason this item is handed off rather than marked
done.

### Runbook

```bash
# 1. Build and install the wrapper (net10.0-android Debug → android-arm64 APK)
dotnet build src/FMS.Mobile/FMS.Mobile.csproj -f net10.0-android -c Debug
# install the APK from
# src/FMS.Mobile/bin/Debug/net10.0-android/android-arm64/com.fms.mobile-Signed.apk

# 2. Check what the WebView actually stored, over USB:
#    desktop Chrome → chrome://inspect → the device → inspect the FMS page
#      Application → Cache Storage  → expect a cache named fms-shell-<hash>
#      Application → IndexedDB      → expect fms-offline → cache / syncMeta
#      Network → reload with the network on: the navigation should read "(ServiceWorker)"
```

**6a. Does the shell load with no connection, and does `CacheModes.NoCache` interfere?**

1. Launch with the network on and let the app finish loading.
2. Turn on airplane mode (or disable Wi-Fi and mobile data).
3. Swipe the app away from recents (a cold kill, not just backgrounding) and relaunch it.
4. **Pass:** the app loads, the header shows the offline banner with a record count and a
   "last synced" age, and no blank WebView or "Couldn't load the app" overlay appears.
5. **If it fails:** the prime suspect is `CacheModes.NoCache` in
   `FmsWebViewHandler.cs` (`LOAD_NO_CACHE` bypasses the WebView's HTTP cache, and the comment
   beside it describes `LOAD_DEFAULT` instead). Change it to the default, rebuild, and repeat.
   If it then passes, keep the change **and** note that a stale bundle becomes possible after a
   deploy — check 6c is what covers that.

**6b. Does the online-only messaging stay honest?**

With airplane mode on and the shell loaded, open a data page (animals, tasks, dashboard).
**Pass:** the page fails or shows empty as it does today, the banner still says saving needs a
connection, and nothing anywhere promises that the attempt was queued. This is the check that
keeps 4.5.1 from overstating itself; it will change when 4.5.2/4.5.4 land, and should be
re-run then.

**6c. Does a deploy reach the device within one online launch?**

GitHub Pages serves `index.html` with `Cache-Control: max-age=600`, and a service worker is
only re-fetched on navigation, so a stale shell is the plausible failure mode.

1. Note the build hash of the deployed bundle (the precache cache name from step 2).
2. Deploy any frontend change to Pages, wait for the workflow to finish, then wait out the
   600 seconds.
3. Launch the app with the network on, then relaunch it.
4. **Pass:** the cache name changes and the visible change appears. **Fail:** the old cache
   name persists across relaunches, which means the worker is not being refreshed and the
   update path needs work before offline writes are built on top of it.

### What to report back

- For each of 6a/6b/6c: pass or fail, the device or emulator used, the Android version, and the
  cache name before and after where applicable.
- Whether `CacheModes.NoCache` had to be changed, and the observation that justified it.
- Anything the banner showed that was not true (a count, an age, or a promise about saving).

---

## Automated QA scripts

Three dependency-free scripts in `scripts/` run against the deployed API
(`node scripts/…`). All three read `FMS_API`, `FMS_EMAIL` and `FMS_PASSWORD`, defaulting to the
Heroku API and the `Test@apk.com` QA account.

| Script | What it does | When it runs |
|---|---|---|
| `qa-survey.mjs` | Read-only breadth: prints the row count of ~40 list endpoints, so an endpoint that answers 200 with nothing is visible at a glance | By hand |
| `qa-seed.mjs` | Fills the QA farm's thin tables (animals, feed, health, HR, finance, inventory, tasks). Idempotent; never updates or deletes; `--dry` plans without writing | By hand |
| `qa-smoke.mjs` | The production contracts, below | Automatically, in `deploy.yml` after a release |

`qa-smoke.mjs` waits out the post-release dyno restart on `/health/live`, then asserts:

1. **Provenance** — `/health` reports the commit this run released. The Docker build bakes it
   in (`ARG GIT_SHA` → `FMS_BUILD_SHA`), the same way the SPA already stamps `VITE_BUILD_SHA`into its bundle. A missing stamp (`unknown`) or a different commit is a failure, never a skip.
2. **CORS preflight** for the Pages origin — the one contract no in-process test can see, and
   the one whose misconfiguration would break the whole web app while every API test stayed green.
3. **The endpoints that have broken in production before** — the two 500s that grouped by a
   computed month label inside the query (`dashboard/charts`, `reports/animals`), the six
   date-only requests that answered 400 because a naive `DateTime` reached a `timestamptz`
   column, and `dashboard/summary`, where `degradedMetrics` must be empty.
4. **The write path** — creates an expense with the exact payload the SPA sends (date-only
   `expenseDate`), updates it, and deletes it in a `finally` that runs even when an assertion
   throws. It asserts by the created id appearing and then disappearing rather than by a row
   count, because the QA farm is shared with people.

Why it exists: `deploy` waits on `test-backend` and `test-client`, so a red test job means **no
container was pushed at all** — which is what happened on the two runs before this was added,
while production kept serving the previous build and nothing in the repository said so. The
smoke job cannot prevent a release (Heroku has already swapped the container by the time it
runs), so it fails the run loudly instead: an `::error::` annotation names the endpoint plus the
API's `traceId` for the Heroku logs, and the job summary lists every check. Recovery stays
deliberate — `heroku releases:rollback web`. To re-check the current release **without** pushing
another container: `gh workflow run deploy.yml -f skip_deploy=true`.

Running it by hand:

```bash
node scripts/qa-smoke.mjs --read-only               # nothing is written
node scripts/qa-smoke.mjs                           # includes create → update → delete
FMS_SMOKE_SHA=<commit> node scripts/qa-smoke.mjs    # also asserts /health reports that commit
FMS_SMOKE_FARM=OtherFarm node scripts/qa-smoke.mjs  # writes only if the account owns that farm
FMS_API=https://<app>.herokuapp.com/api node scripts/qa-smoke.mjs
```

`FMS_API` is the app's own `.../api` URL — the same value `client/.env.production` is built with.
CI passes it directly rather than assembling it from `HEROKU_APP_NAME`, because the Heroku CLI
accepts an app's *hostname* in `--app` as readily as its name: deriving the URL produced
`https://<host>.herokuapp.com`, and the smoke job's first run reported "No such app" about a host
that does not exist while the release it was verifying was live and correct. A request that never
reached the API is reported as such, not as an endpoint failure. Override the URL with the
`FMS_SMOKE_API_URL` secret if the app ever moves.

---

## Results log

Fill in as each item is executed. Do not mark an item verified on inspection alone.

| Item | Date | Who | Outcome | Notes |
|---|---|---|---|---|
| 1. PostgreSQL suite | | | | |
| 2. CI gate | 2026-09-21 | agent | **DONE** | 5 runs; gate proven red→green; client-suite flake found |
| 3. compose stack | | | | |
| 4. Secret rotation | | | | |
| 5. Live email delivery | | | | reset/invitation/digest to a real inbox; blocked on the two preconditions above |
| 6. Offline shell (device) | | | | three checks: shell loads offline × `CacheModes.NoCache`, messaging stays honest, a deploy reaches the device |
