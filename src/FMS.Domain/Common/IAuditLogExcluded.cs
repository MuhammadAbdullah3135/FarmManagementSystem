namespace FMS.Domain.Common;

/// <summary>
/// Marks an entity as internal system telemetry rather than user-facing
/// business data, so <c>AuditLogInterceptor</c> skips it.
///
/// Background jobs write rows with no HTTP principal (no user id, no email, no
/// IP). Auditing business records a job creates is correct and desirable, but
/// auditing the job's own bookkeeping rows would flood the audit log with
/// entries attributed to nobody.
/// </summary>
public interface IAuditLogExcluded
{
}
