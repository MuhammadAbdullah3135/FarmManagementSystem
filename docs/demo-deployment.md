# Demo deployment plan

Status: **proposal only — nothing in this document has been executed.** It describes a
separate, public demo deployment for the shared demo account. It deliberately does not change
the production API, the production database, or production code.

## Goal

Publish a public link a reviewer can open, click **Try demo**, and explore FMS with sample
data. The demo must be isolated from production in every respect: its own API instance, its own
database, its own JWT secret, its own email behaviour, and its own rate limits and CORS.

## Verified starting point

- The demo is feature-gated behind `Demo:Enabled` (`false` in `src/FMS.API/appsettings.json`
  and `.env.example`; `true` in `appsettings.Development.json`).
- With `Demo:Enabled=false`, `POST /api/auth/demo-login` and `POST /api/auth/demo-reset`
  return **410 Gone**, not 404.
- The demo account is created lazily on the first demo login and owns one farm that
  `POST /api/auth/demo-reset` restores to seed state.
- The API runs EF Core migrations automatically at startup (`Database.MigrateAsync()` in
  `src/FMS.API/Program.cs`). Hangfire installs its schema in the same database afterwards.
- Existing deployment config in the repo is **Heroku (API, Docker) + GitHub Pages (client)**.
  No other hosting target is configured in the repo.

## 1. Isolated API instance and database

Provision a **new** API instance (for example a second Heroku app, or another container host)
and a **new, empty PostgreSQL database** dedicated to the demo.

Hard isolation requirements:

- **No shared credentials with production.** New database user/password, new JWT secret, new
  API platform credentials/config vars.
- **No shared database with production.** The demo connection string must point at the new
  database only. Do not reuse a production database, schema, or role.
- **No shared object storage.** Either use `Storage__Provider=Local` (sample data does not
  upload files by default) or a dedicated bucket/prefix. Never point the demo at production
  uploads.
- **No shared email sender.** Use `Email__Provider=Log` (renders messages to the log, sends
  nothing) for the demo. Do not supply a SendGrid key.
- **No shared background-job state.** Hangfire runs in-process against the demo database;
  `Jobs__Enabled=true` is acceptable, but note it will create the `hangfire` schema there.

Initial database creation and migration are **not** part of this plan's execution. The first
API start will run migrations against the new empty database. That is a separate, explicit step
that must be approved before it runs.

## 2. Environment variables for the demo API

| Variable | Value for the demo instance | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Matches the image default. |
| `Demo__Enabled` | `true` | Enables the demo endpoints. |
| `Demo__DemoEmail` | `demo@farm.local` (default) or a demo-only address | Fixed identity used by the seeder. |
| `ConnectionStrings__DefaultConnection` | New demo database connection string | Must not contain production credentials. |
| `Jwt__SecretKey` | **Newly generated** for the demo instance | e.g. `openssl rand -base64 48`; never reuse the production key. |
| `Jwt__Issuer` / `Jwt__Audience` | `FarmManagementSystem` (defaults) | Keep consistent within the demo instance. |
| `Email__Provider` | `Log` | No real email leaves the demo. |
| `Email__FromAddress` | Empty or a non-delivering address | Required only if a real provider is selected. |
| `Frontend__BaseUrl` | Public URL of the demo client | Used for links in any rendered messages. |
| `Cors__AllowedOrigins__0` | Public origin of the demo client (e.g. `https://<user>.github.io`) | Origin only, **no path**; the GitHub Pages app lives under `/FarmManagementSystem/`. |
| `Storage__Provider` | `Local` | Sample data does not need durable uploads; local disk is ephemeral. |
| `ForwardedHeaders__Enabled` | `true` | Trust the proxy's `X-Forwarded-For`/`X-Forwarded-Proto`; required for per-IP rate limits behind Heroku. |
| `ForwardedHeaders__TrustAllProxies` | `true` | Heroku's router addresses are not stable; the dyno is not directly reachable. |
| `ForwardedHeaders__ForwardLimit` | `1` | One proxy hop (the Heroku router). |
| `Jobs__Enabled` | `true` or `false` | Either works; if `false`, scheduled feed-task/alert jobs do not run. |
| `FMS_BUILD_SHA` | Commit SHA of the released image | Optional; `/health` reports it. |

Do **not** copy production config vars wholesale. In particular, do not copy production
`Jwt__SecretKey`, the production connection string, or SendGrid credentials.

## 3. Client build

Build the client with a demo-specific API URL; **do not use `client/.env.production` as-is**
(it points at the production Heroku API).

- Set `VITE_API_URL=https://<demo-api-host>/api` for the build.
- The Vite `base` is `/FarmManagementSystem/`; a GitHub Pages deployment would be served under
  that subpath, so the public link is `https://<user>.github.io/FarmManagementSystem/`.
- Keep the SPA fallback (`dist/index.html` copied to `dist/404.html`) if deploying to GitHub
  Pages.
- Prefer a dedicated build/deploy target (a separate Pages site, a branch/environment, or
  another static host) so the production Pages site is not overwritten with a demo build.
  If a separate Pages deployment is not possible, this becomes an open question (below).

## 4. Rate limits and CORS for a public URL

Already implemented in code:

- `POST /api/auth/demo-login` — fixed window, **10 requests/minute per remote IP**, HTTP 429
  when exceeded.
- `POST /api/auth/demo-reset` — fixed window, **5 requests/minute per remote IP**, HTTP 429
  when exceeded.
- CORS reads `Cors:AllowedOrigins` and allows credentials and the message-key headers.

For a public URL:

- Set `Cors__AllowedOrigins__0` to the **exact** demo client origin (scheme + host + port if
  non-default), not a wildcard. Add more indexed entries if the demo is reachable from more
  than one origin.
- Confirm whether the host sits behind a proxy/load balancer. The demo limits partition on
  `HttpContext.Connection.RemoteIpAddress`. The API now supports opt-in forwarded headers
  (`ForwardedHeaders:Enabled=true` + `TrustAllProxies=true` for Heroku) so the limiter sees the
  visitor's address rather than the proxy's; `src/FMS.API/Networking/ForwardedHeadersSetup.cs`
  implements it and `ForwardedHeadersTests` covers the partition behaviour. Leave it disabled
  for a directly exposed deployment.
- Consider platform-level request/timeout limits as a second layer, but do not rely on them
  instead of the in-app policies.

## 5. Seed and verify

1. Deploy the demo API image with the environment above, pointing at the new empty database.
2. On first start, EF migrations run and roles are seeded. This writes only to the demo
   database.
3. Seed the demo data by making **one** sandbox login call; the seeder creates the user,
   account, farm, and sample data lazily:
   ```bash
   curl -sS -X POST "https://<demo-api-host>/api/auth/demo-login" \
        -H "Content-Type: application/json" -d '{}'
   ```
4. Verify the response contains a demo JWT and `isDemo: true`, and that the farm has data
   (for example, log in through the demo client link and confirm animals/tasks appear).
5. Verify the reset path once, using the returned token:
   ```bash
   curl -sS -X POST "https://<demo-api-host>/api/auth/demo-reset" \
        -H "Authorization: Bearer <demo-token>"
   ```
6. Confirm the demo client loads, the **Demo** banner is visible, and **Try demo** works from
   a clean browser profile.

## 6. Rollback / disable

The demo can be disabled without deleting anything:

1. Set `Demo__Enabled=false` on the demo API instance and restart/redeploy it.
2. Both demo endpoints then return **410 Gone**; the demo client still loads but
   **Try demo** fails with the generic demo failure message.
3. To take the public link down entirely, unpublish/remove the demo client deployment and stop
   the demo API instance/database.

No production setting is touched by this rollback.

## 7. Open questions (blocking execution)

1. **Which host** should run the demo API — a second Heroku app, the same host as production
   under a different app, or something else?
2. **Who provides/pays for** the demo host and the new PostgreSQL database (and how long it
   should stay up)?
3. **Where should the demo client be served** without overwriting the production GitHub Pages
   site — a separate Pages site/repo, a branch deployment, or another static host?
4. **May I push `bc87865`** ("Document the demo account in the README") to
   `origin/feature/demo-account`? It is currently local-only, one commit ahead.
5. **May the first API start run migrations** against the new empty demo database (automatic
   on boot), and should the demo database be created by me or provisioned for me?
6. If the chosen host proxies requests, **do you want forwarded-header support added** so the
   per-IP demo rate limits work correctly, or should we accept a shared bucket for now?
