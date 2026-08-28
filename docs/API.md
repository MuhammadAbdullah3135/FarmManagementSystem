# FMS API Documentation

## Authentication
All API requests require a JWT Bearer token in the Authorization header.

### Endpoints
- POST /api/auth/register - Create account
- POST /api/auth/login - Get access + refresh tokens
- POST /api/auth/refresh - Refresh expired access token
- POST /api/auth/reset-password - Request password reset
- GET /api/auth/me - Get current user info

## Farm Context
All farm-scoped endpoints require the X-Farm-Id header.

## Rate Limiting
- General API: 300 requests/minute per user
- Returns 429 with Retry-After header when exceeded

## Health Checks
- GET /health - Full health check with DB connectivity
- GET /health/ready - Readiness probe (DB check)
- GET /health/live - Liveness probe (always healthy)

## Error Responses
All errors follow RFC 7807 Problem Details format with traceId for debugging.
