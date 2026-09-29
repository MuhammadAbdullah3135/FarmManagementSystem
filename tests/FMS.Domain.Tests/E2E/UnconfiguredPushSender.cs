using FMS.Application.Notifications;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The push channel as an unconfigured deployment sees it: off, and loud if anything
/// tries to use it anyway.
///
/// <para>
/// The E2E host builds its own service graph rather than reusing <c>Program.cs</c>, so
/// the push registrations have to be repeated here or the controller cannot even be
/// constructed — which is exactly how this was found: every notification route answered
/// 500 with "Unable to resolve service for type … IPushSubscriptionService". Pairing the
/// real subscription service with a sender that reports itself unconfigured keeps the
/// HTTP surface exercised over the same code an operator gets before they supply VAPID
/// keys.
/// </para>
///
/// <para>
/// <see cref="SendAsync"/> throwing rather than returning a failure is the point: the
/// dispatcher must consult <see cref="IsConfigured"/> before it sends anything, and a
/// regression that pushed anyway would fail here instead of silently delivering nothing.
/// </para>
/// </summary>
public sealed class UnconfiguredPushSender : IPushSender
{
    public bool IsConfigured => false;

    public Task<PushSendResult> SendAsync(
        PushSubscriptionTarget target,
        PushMessage message,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Push is not configured in the E2E host; nothing should be attempting a send.");
}
