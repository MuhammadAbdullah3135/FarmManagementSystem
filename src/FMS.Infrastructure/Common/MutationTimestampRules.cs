namespace FMS.Infrastructure.Common;

/// <summary>
/// The one place that decides whether a device-supplied timestamp is plausible.
///
/// Queued mutations carry the time the work actually happened, not the time the server
/// received it — a weight taken at 06:55 must not be recorded as 14:00 because that is
/// when the device found signal. That makes the timestamp client-controlled, so every
/// path that accepts one has to reject a clock that is obviously wrong.
///
/// The tolerance below is the rule the weight endpoint already applied (a small allowance
/// for the device clock running ahead of the server's), extracted here so attendance and
/// task completion cannot answer with a different standard — the same reason the import
/// rules were extracted: two callers, one rule, no drift.
///
/// <para>
/// The messages stay per workflow: "Recorded date cannot be in the future" is the weight
/// endpoint's existing text and must not change for inputs it already rejected, while a
/// check-in and a check-out need to name their own field.
/// </para>
/// </summary>
public static class MutationTimestampRules
{
    /// <summary>How far ahead of the server a device timestamp may be before it is refused.</summary>
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    public const string WeightRecordedAtMessage = "Recorded date cannot be in the future";
    public const string CheckInOccurredAtMessage = "Check-in time cannot be in the future";
    public const string CheckOutOccurredAtMessage = "Check-out time cannot be in the future";
    public const string TaskCompletedAtMessage = "Completion time cannot be in the future";

    /// <summary>
    /// True when the timestamp is far enough ahead of <paramref name="nowUtc"/> that no
    /// plausible clock could explain it.
    /// </summary>
    public static bool IsTooFarInTheFuture(DateTime timestamp, DateTime nowUtc) =>
        timestamp > nowUtc.Add(MaxFutureSkew);
}
