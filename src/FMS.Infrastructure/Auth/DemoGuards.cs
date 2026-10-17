using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Auth;

/// <summary>
/// Central place for the restrictions that apply to the shared demo account.
///
/// The demo user is a single flagged account (<c>User.IsDemo</c>) reached only through
/// <c>DemoLoginAsync</c>. Writes made in a demo session are isolated by farm (the demo user
/// owns the demo farm, which is separate from any real account's farms), and the demo farm is
/// restored to seed state by <c>DemoDataSeeder.ResetDemoDataAsync</c>. The restrictions below
/// are the additional layer that blocks irreversible or sensitive actions that should never run
/// for a visitor exploring the application:
/// <list>
///   <item><description>Sending real email (password reset, invitation, alert digest).</description></item>
///   <item><description>Changing the demo user's password.</description></item>
///   <item><description>Deleting the demo account (no endpoint today, but guarded for the future).</description></item>
///   <item><description>Requesting a full-farm export of the demo farm.</description></item>
///   <item><description>Recording a real payment (a salary payment); there is no payment gateway
///   in this build, so the money-moving action is the salary-payment ledger.</description></item>
/// </list>
///
/// Each guard returns a <c>Result</c> whose <c>Code</c> is one of the <c>validation.demo.*</c>
/// keys so the client can render the restriction in the reader's language. Callers that cannot
/// proceed return that result; callers that can proceed ignore it.
/// </summary>
public static class DemoGuards
{
    /// <summary>
    /// True when the current request is authenticated as the shared demo user.
    /// Reads <c>User.IsDemo</c> from the database for the current principal's user id, so the
    /// check is authoritative rather than trusting the bearer token alone.
    /// </summary>
    public static async Task<bool> CurrentUserIsDemoAsync(FmsDbContext context, Guid userId)
    {
        return await IsDemoUserAsync(context, userId);
    }

    /// <summary>
    /// True when <paramref name="userId"/> is the shared demo user. Reads the real
    /// <c>User.IsDemo</c> flag from the database — it does not trust a caller-supplied value —
    /// so an unauthenticated or naive caller cannot widen a demo session into a real one.
    /// </summary>
    public static async Task<bool> IsDemoUserAsync(FmsDbContext context, Guid userId)
    {
        return await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsDemo);
    }

    /// <summary>
    /// Shared refusal used by the password-change path: the demo user may not change their own
    /// password through the reset flow.
    /// </summary>
    public static Result DemoPasswordChangeBlocked() => Result.Validation(
        "Changing the demo account password is disabled.",
        DomainMessageKeys.DemoPasswordChangeDisabled);

    /// <summary>
    /// Shared refusal used by the password-reset-request path: the demo account does not receive
    /// password reset emails (it has no password to reset, and the demo login is credential-free).
    /// Returns a failure rather than a silent success so the caller cannot mistake "nothing was
    /// sent" for "a reset is on its way" for the one address that is known to exist.
    /// </summary>
    public static Result DemoPasswordResetBlocked() => Result.Validation(
        "Resetting the demo account password is disabled.",
        DomainMessageKeys.DemoPasswordResetDisabled);

    /// <summary>
    /// Refuses sending an email because the recipient is the demo address (or the current user is
    /// the demo user and the email path is a real-email action). The exact message depends on the
    /// call site; this helper returns the common shape.
    /// </summary>
    public static Result DemoEmailSendingBlocked() => Result.Validation(
        "Email sending is disabled for the demo account.",
        DomainMessageKeys.DemoEmailSendingDisabled);

    /// <summary>
    /// Refuses a real payment (a salary payment recorded today is the only money-moving action
    /// this build has; there is no payment gateway) because the current caller is the demo user.
    /// The demo farm's ledger is sample data, and a visitor exploring the app must not be able to
    /// post a payment that looks as if real money moved.
    /// </summary>
    public static Result DemoRealPaymentsBlocked() => Result.Validation(
        "Real payments are disabled for the demo account.",
        DomainMessageKeys.DemoStaffRealPaymentsDisabled);

    /// <summary>
    /// Refuses a full-farm export because the current caller is the demo user. Ordinary report
    /// pages are not affected — only the bulk ZIP export is restricted.
    /// </summary>
    public static Result DemoExportBlocked() => Result.Validation(
        "Real data export is disabled for the demo account.",
        DomainMessageKeys.DemoRealExportDisabled);

    /// <summary>
    /// Refuses account deletion because the target is the demo user. No account-deletion endpoint
    /// exists in this build, but this helper is the place a future one should call so the
    /// restriction is stated once.
    /// </summary>
    public static Result DemoDeleteAccountBlocked() => Result.Validation(
        "Deleting the demo account is disabled.",
        DomainMessageKeys.DemoDeleteAccountDisabled);
}
