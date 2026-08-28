# FMS Security Notes

## SQL Injection Prevention
- **Status**: Confirmed safe. No raw SQL anywhere in the codebase.
- All queries use EF Core LINQ which is automatically parameterized.
- No `FromSqlRaw`, `ExecuteSqlRaw`, or string-concatenated queries found.

## Authentication & Authorization
- JWT Bearer tokens with 15-minute access token expiry
- 30-day refresh token expiry with rotation on use
- ClockSkew = 0 for precise token validation
- Role-based access: SystemOwner, FarmManager, Veterinarian, Employee, Accountant, Viewer

## Audit Logging
- Every Create/Update/Delete is captured automatically via EF Core interceptor
- Logs include: who, what, when, old values, new values, IP address
- Restricted to SystemOwner, FarmManager, Accountant roles

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
