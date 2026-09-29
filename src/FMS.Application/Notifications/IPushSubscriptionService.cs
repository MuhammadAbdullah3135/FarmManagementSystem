using FMS.Application.Common;

namespace FMS.Application.Notifications;

/// <summary>
/// Registers and lists the caller's own push devices.
///
/// <para>
/// Every method takes the user explicitly and the implementation filters on it, for
/// the same reason <see cref="INotificationService"/> does: a device stands for a
/// person, and a subscription id from the request must never be able to reach
/// somebody else's.
/// </para>
///
/// <para>
/// The farm in the signature is route context, not ownership: a subscription belongs
/// to a user, so that the phone in a pocket keeps receiving alerts from every farm
/// that user works on. Which of those alerts is worth a buzz stays per farm, in the
/// preferences matrix the dispatcher consults.
/// </para>
/// </summary>
public interface IPushSubscriptionService
{
    /// <summary>Whether push is configured at all, the public key to subscribe with, and the caller's devices.</summary>
    Task<Result<PushSettingsDto>> GetSettingsAsync(Guid farmId, Guid userId);

    /// <summary>
    /// Registers or refreshes one device. Idempotent on the endpoint: a browser that
    /// re-subscribes updates its keys and label instead of adding a row.
    /// </summary>
    Task<Result<PushSubscriptionDto>> RegisterAsync(
        Guid farmId,
        Guid userId,
        RegisterPushSubscriptionRequest request);

    /// <summary>
    /// Removes one of the caller's devices. Used when a user turns push off in the
    /// browser: unsubscribing locally must also stop the server, or the next dispatch
    /// spends its time on an endpoint the browser has already discarded.
    /// </summary>
    Task<Result> UnregisterAsync(Guid farmId, Guid userId, Guid subscriptionId);
}
