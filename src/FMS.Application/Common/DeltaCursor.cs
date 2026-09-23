namespace FMS.Application.Common;

/// <summary>
/// When a cached read may be answered as a delta, and when it must be answered in full.
///
/// <para>
/// A delta is only honest while the cursor it compares against still covers everything that
/// changed. Two cursors do not: one from further back than <see cref="MaxAgeDays"/> (the
/// response could be arbitrarily large, and the client should simply take a fresh copy), and
/// one from the future (a device whose clock ran fast, or a client that sent something it
/// merely parsed). Both are answered with a full-sync demand rather than an empty delta —
/// answering "nothing changed" to a cursor the server cannot interpret is the one reply that
/// loses data silently.
/// </para>
///
/// <para>
/// The window is deliberately far shorter than the audit log's own retention: the window is
/// about how much a device should be sent at once, not about how far back history goes.
/// </para>
/// </summary>
public static class DeltaCursor
{
    public const int MaxAgeDays = 30;

    /// <summary>
    /// How many changed rows a delta will return before it gives up and asks for a full read.
    ///
    /// <para>
    /// A delta is not paged, because paging one would need the client to walk every page before
    /// it may advance its cursor — get that wrong and a row changed between two pages is never
    /// delivered. Instead the delta is bounded: past this many changed rows the honest answer is
    /// "take a fresh copy", which is also smaller on the wire than the deltas it replaces.
    /// A farm that changes more than this within one sync window is not one where a delta read
    /// was buying anything.
    /// </para>
    /// </summary>
    public const int MaxDeltaRows = 500;

    /// <summary>True when <paramref name="since"/> can be answered precisely.</summary>
    public static bool IsAnswerable(DateTime? since, DateTime now) =>
        since is { } value
        && value <= now
        && value > now.AddDays(-MaxAgeDays);
}
