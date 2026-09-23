namespace FMS.Application.Sync;

/// <summary>
/// The workflows the sync endpoint may apply, and the farm roles each one requires.
///
/// <para>
/// This table is the sync endpoint's half of the authorization contract. A farm-scoped route
/// inherits <c>[Authorize]</c>, but that only says "a member of this farm"; the per-workflow
/// role a single-record endpoint carries lives on <em>its</em> controller, and a queued item
/// never passes through that controller. So the requirement is restated here per operation and
/// the runtime gate below enforces it — and a test asserts this table against the three
/// controllers' own <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/>s, so
/// the day one of them gains a role this fails loudly instead of quietly widening the sync path.
/// </para>
///
/// <para>
/// All four operations currently require no role beyond farm membership, which is exactly what
/// <c>AnimalsController</c>, <c>AttendanceController</c> and <c>TasksController</c> declare.
/// </para>
/// </summary>
public static class SyncOperations
{
    public const string WeightRecord = "weight.record";
    public const string AttendanceCheckIn = "attendance.checkIn";
    public const string AttendanceCheckOut = "attendance.checkOut";
    public const string TaskComplete = "task.complete";

    public static readonly IReadOnlyList<string> All = new[]
    {
        WeightRecord,
        AttendanceCheckIn,
        AttendanceCheckOut,
        TaskComplete
    };

    public static bool IsKnown(string? operation) =>
        operation is not null && All.Contains(operation);

    /// <summary>
    /// The roles a caller must hold for <paramref name="operation"/>, or null when any member
    /// of the farm may perform it. Null is not "no rule" — it is "the same rule the workflow's
    /// own controller declares".
    /// </summary>
    public static IReadOnlyList<string>? RequiredRoles(string operation) => operation switch
    {
        WeightRecord or AttendanceCheckIn or AttendanceCheckOut or TaskComplete => null,
        _ => null
    };
}
