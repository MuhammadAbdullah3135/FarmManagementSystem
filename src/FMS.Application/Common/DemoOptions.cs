namespace FMS.Application.Common;

/// <summary>
/// Feature gate and demo identity for the shared demo account.
///
/// <list type="bullet">
/// <item><description><c>Enabled</c> — when <c>true</c>, <c>POST /auth/demo-login</c> and
/// <c>POST /auth/demo-reset</c> are reachable; when <c>false</c> they return 410 Gone so a
/// deployment that never wanted a demo simply has no reachable demo endpoint.</description></item>
/// <item><description><c>DemoEmail</c> — the fixed demo address, used in logs, the demo user
/// record, and the login response so traces are readable.</description></item>
/// <item><description><c>DemoPasswordHash</c> — a fixed BCrypt hash the demo user's PasswordHash is
/// set to at seed time. Its only purpose is to occupy <c>PasswordHash</c> so the user record is
/// valid and so the real <c>LoginAsync</c> password branch would reject the demo address: a visitor
/// can only reach the demo user through <c>DemoLoginAsync</c>, never through the real login path.
/// The plaintext is deliberately not stored anywhere; this is not a real credential.</description></item>
/// </list>
/// </summary>
public class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }
    public string DemoEmail { get; set; } = "demo@farm.local";
    public string DemoPasswordHash { get; set; } =
        // bcrypt hash of "demo-farm-password". Generated once and pinned so the seed is stable
        // across runs and across environments. The plaintext is not stored anywhere in the repo.
        "$2a$11$F1Xdk1aWWr8W1R9yyjcgb.CflwM6M/wDtH/XE9zlI7sC/..9qU.Be";
}
