# FMS Security Notes

## SQL Injection Prevention
- **Status**: Confirmed safe. No raw SQL anywhere in the codebase.
- All queries use EF Core LINQ which is automatically parameterized.
- No `FromSqlRaw`, `ExecuteSqlRaw`, or string-concatenated queries found.

## Secret Management
- **The JWT signing key is validated at startup.** `JwtSigningKeyGuard` runs before any service is registered and refuses to start outside Development when `Jwt:SecretKey` is missing, shorter than 32 bytes, or still the committed development placeholder. Without it, a deployment that forgot `Jwt__SecretKey` would sign every access *and* refresh token with a key published in this repository — and a token signed with a public key is not a weak credential, it is no credential, since any reader of the repository can mint a SystemOwner token.
- The development key lives in `appsettings.Development.json` and is accepted **only** in Development. `appsettings.json` deliberately carries no key, so production must supply `Jwt__SecretKey` explicitly.
- `docker-compose.yml` takes `DB_PASSWORD` and `JWT_SIGNING_KEY` from an untracked `.env` and aborts at startup when either is empty. No credential literal is committed.
- `TrackedConfigDriftTests` asserts these properties, so a committed placeholder or a SQL-Server-shaped connection string cannot reappear unnoticed.
- **Two secrets are in git history and should be treated as compromised**: the SQL Server SA password `YourStrong!Password123` (added `0d06b20`, removed from tracked files in `23c2efc`) and the JWT placeholder above. Production is Heroku + PostgreSQL, so the SA password only ever applied to the retired local SQL Server container — but rotation, not history rewriting, is what makes the old values worthless. Steps and current status: [docs/VERIFICATION.md](docs/VERIFICATION.md).

## Authentication & Authorization
- JWT Bearer tokens with 15-minute access token expiry
- 30-day refresh token expiry with rotation on use
- ClockSkew = 0 for precise token validation
- Role-based access: SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer

## Audit Logging
- Every Create/Update/Delete is captured automatically via EF Core interceptor
- Logs include: who, what, when, old values, new values, IP address
- Restricted to SystemOwner, FarmManager, Accountant roles
- Internal telemetry (the `FarmHealthStatusSnapshot` written by the health job) is excluded via `IAuditLogExcluded`: a background job has no principal to attribute it to, so logging it would put phantom user-less rows in the audit trail

## Scheduled Jobs
- `/api/admin/jobs` (job status) is restricted to the SystemOwner **account** role. It is account-scoped — no `{farmId}` in the route — so the farm-context gate intentionally does not apply and a farm role can never grant it.
- The Hangfire dashboard is mounted in Development only. A browser navigation cannot carry the SPA's bearer token, so in Development it is limited to loopback requests; production SystemOwners use the JSON endpoint instead.
- Jobs run in their own DI scope per farm, so a job execution has no HTTP principal and cannot inherit a caller's farm context.

## Notifications
- Every notification endpoint is farm-scoped (`/api/farm/{farmId}/notifications…`) and needs no exemption from the farm-context gate, so it inherits the same header/route enforcement as the rest of the farm surface.
- The recipient is always the authenticated user: the service filters every read and write by the token's `NameIdentifier`, and a notification belonging to another member answers 404 rather than confirming it exists.
- Alert delivery is scoped to the farm's members whose role can act on the alert type (mirroring the controller role attributes for inventory, and excluding the read-only Viewer role elsewhere). An email digest is the only out-of-app channel; its transport is still a logging placeholder, so no message leaves the process until a provider is configured.

## Rate Limiting
- Global: 300 requests/minute per authenticated user (sliding window)
- Mobile sync reconnect scenario accommodated

## CORS
- Configurable via appsettings (no hardcoded origins in production)
- Only configured origins can access the API

## HTTPS
- HSTS enabled in non-Development environments
- HTTPS redirection enforced

## Security Headers
- X-Content-Type-Options: nosniff
- X-Frame-Options: DENY
- X-XSS-Protection: 1; mode=block
- Referrer-Policy: strict-origin-when-cross-origin
- Content-Security-Policy (basic)

## Input Validation
- FluentValidation validators on all Create/Update DTOs
- Server-side validation rejects invalid payloads with clear error messages
