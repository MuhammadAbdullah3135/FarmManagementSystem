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
- Alert delivery is scoped to the farm's members whose role can act on the alert type (mirroring the controller role attributes for inventory, and excluding the read-only Viewer role elsewhere). Email is the only out-of-app channel; whether it leaves the process depends on `Email:Provider` (see **Email Delivery** below).

## Email Delivery
- **The provider API key is a bearer credential for sending as the configured sender.** It is read from `Email__SendGrid__ApiKey`, never from a committed file: `appsettings.json` ships `Email:Provider = Log` with an empty key and sender, so the transport sends nothing until credentials are supplied explicitly. `EmailConfigurationDriftTests` asserts that, and scans every tracked config file for anything shaped like a sendable key.
- **The key never reaches a log line or an exception.** The transport logs the recipient, the provider's status and its message id, and nothing else; the request body and the `Authorization` header are excluded. `SendGridEmailTransportTests` runs every path — success, rate-limited-then-success, a rejected key, exhausted retries, an unreachable host and a timeout — with a capturing logger and scans every entry and every exception for the key, rather than relying on code review.
- **A provider that cannot work is a startup failure outside Development** (`EmailConfigurationGuard`), so a deployment cannot quietly deliver no password reset while reporting success. Inside Development it downgrades to the log transport with a warning, so a half-configured checkout still runs.
- Messages carry links back into the frontend, so `Frontend:BaseUrl` must be the deployed URL; the guard warns while it is unset or still the template. The same value backs the reset and invitation links.
- A failed send never fails its request, and never loses durable state: the reset token and invitation row are written before the send, the transport throws, and the caller logs and continues — the same asymmetry the alert digest already used. Failing the request instead would also turn `POST /auth/forgot-password` into an account-existence oracle, since a send is only attempted for addresses that exist.
- **Deliverability is delegated to the provider's sender verification.** `Email:FromAddress` must be a verified address (or on a verified domain); an unverified sender is rejected at send time, which the retry policy deliberately does *not* retry.

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

## Bulk import
- **The import adds no authorization surface.** Each import controller carries exactly the authorization of its single-record counterpart (`[Authorize]` for animals and employees, the inventory roles for inventory items), asserted by a test that compares the attributes rather than a comment that promises it. Importing is therefore never a way around a role that creating one row is subject to.
- **Row validation is the create endpoint's own rule function**, not a parallel copy: animals, employees and inventory items each have one rule set shared by the single-record path and the batch path, so the two cannot drift into "the endpoint refuses what the import accepts".
- **An identifier is unique in both paths.** A tag number, an item name or an employee email already present — or repeated inside the same file — is a row error: never skipped silently, never overwritten. Soft-deleted employees are excluded, so re-adding someone who left is allowed.
- **The commit re-reads and re-validates the uploaded bytes** rather than trusting the preview, and writes the batch in one `SaveChanges`. A record created between the two calls therefore blocks the import instead of being duplicated.
- **Uploads are bounded**: file size, row count and header/column bounds are checked before anything is written, and no file content is interpolated into SQL or into a response body — the report carries the values back as data.
- Known gap, deliberate and bounded: the employee email uniqueness rule is enforced by both code paths but has **no database unique index** behind it, unlike inventory's `(FarmId, Name)`. Two concurrent creates could in principle both pass the check. Adding that index is a migration, deliberately left out of this subphase.
