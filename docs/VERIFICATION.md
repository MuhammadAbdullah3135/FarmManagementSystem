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

## Results log

Fill in as each item is executed. Do not mark an item verified on inspection alone.

| Item | Date | Who | Outcome | Notes |
|---|---|---|---|---|
| 1. PostgreSQL suite | | | | |
| 2. CI gate | 2026-09-21 | agent | **DONE** | 5 runs; gate proven red→green; client-suite flake found |
| 3. compose stack | | | | |
| 4. Secret rotation | | | | |
