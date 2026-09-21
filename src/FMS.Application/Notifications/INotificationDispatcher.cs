namespace FMS.Application.Notifications;

/// <summary>
/// What one dispatch run did for one farm. Returned so tests can assert the
/// reconciliation outcome directly instead of inferring it from database state.
/// </summary>
public readonly record struct NotificationDispatchResult(
    int Created,
    int Updated,
    int Resolved,
    int EmailsSent,
    int EmailFailures,
    IReadOnlyList<string> SkippedAlertTypes)
{
    public static NotificationDispatchResult Empty { get; } =
        new(0, 0, 0, 0, 0, Array.Empty<string>());
}

/// <summary>
/// Evaluates a farm's alert conditions and reconciles its notifications.
///
/// Scoped to a single farm on purpose: the dispatcher runs once per farm in its
/// own service scope, so no run ever reads across farms and one farm's failure
/// cannot affect another's.
/// </summary>
public interface INotificationDispatcher
{
    Task<NotificationDispatchResult> ExecuteAsync(Guid farmId, CancellationToken cancellationToken = default);
}
