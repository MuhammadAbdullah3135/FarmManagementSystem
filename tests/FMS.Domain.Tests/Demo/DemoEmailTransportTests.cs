using FMS.Application.Auth;
using FMS.Application.Email;
using FMS.Application.Notifications;
using FMS.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FMS.Domain.Tests.Demo;

/// <summary>
/// The transport-level half of the demo email restriction.
///
/// <see cref="EmailService.IsDemoRecipient"/> short-circuits all three send paths when the
/// recipient is the configured demo address, returning without touching
/// <see cref="IEmailTransport"/>. The dispatch is deliberately silent (no exception, no
/// failure result), so the only externally observable fact is that the transport was never
/// asked to deliver — which is exactly what these tests pin, with a normal address as the
/// control so a transport that is never called at all cannot pass.
/// </summary>
public class DemoEmailTransportTests
{
    private const string DemoEmail = "demo@farm.local";
    private const string RealEmail = "owner@example.com";

    private sealed class RecordingTransport : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static (EmailService Service, RecordingTransport Transport) Create()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:DemoEmail"] = DemoEmail,
                ["Frontend:BaseUrl"] = "https://farm.example.com",
                ["Invitations:ExpiryDays"] = "7",
            })
            .Build();

        var transport = new RecordingTransport();
        return (new EmailService(transport, configuration, NullLogger<EmailService>.Instance), transport);
    }

    /// <summary>Runs all three send paths, so the guard is proven on each one, not just one.</summary>
    private static async Task SendAllAsync(EmailService service, string email)
    {
        await service.SendPasswordResetEmailAsync(email, "https://farm.example.com/reset?token=abc");
        await service.SendFarmInvitationEmailAsync(email, "Sunrise Ranch", "https://farm.example.com/accept-invitation?token=abc");
        await service.SendAlertNotificationsEmailAsync(
            email,
            "Sunrise Ranch",
            new[] { new NotificationEmailItem(NotificationSeverity.Critical, "Low feed", "Feed is low", "/dashboard/feed") });
    }

    [Fact]
    public async Task Emails_addressed_to_the_demo_address_never_reach_the_transport()
    {
        var (service, transport) = Create();

        await SendAllAsync(service, DemoEmail);

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Emails_addressed_to_a_normal_address_reach_the_transport()
    {
        var (service, transport) = Create();

        await SendAllAsync(service, RealEmail);

        Assert.Equal(3, transport.Sent.Count);
        Assert.All(transport.Sent, message => Assert.Equal(RealEmail, message.To));
    }
}
