namespace FMS.Application.Email;

/// <summary>A rendered email, ready to hand to a provider.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The provider seam. The application's email service renders the three messages it
/// sends - password reset, farm invitation, and the alert digest - and a transport
/// delivers them. Keeping the two apart is what lets the delivery mechanism be
/// replaced (or switched off) without touching the calling code, and lets the
/// rendering be tested without a network.
///
/// A transport throws <see cref="EmailDeliveryException"/> when delivery fails. It
/// must not swallow failures: the notification dispatcher records the error against
/// the notification rows, and a transport that returned quietly would instead mark
/// undelivered mail as delivered.
/// </summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
