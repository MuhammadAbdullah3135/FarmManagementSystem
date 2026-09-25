using FMS.Application.Dashboard;
using FMS.Application.Farm;

namespace FMS.Application.Notifications;

/// <summary>
/// The notification system's vocabulary.
///
/// These are exactly the alert types <see cref="IDashboardService.GetAlertsAsync"/>
/// already computes — no new conditions are introduced here. Notifications are a
/// durable record of the same alerts, so adding a condition to one without the
/// other would immediately make the two disagree.
///
/// This type also owns the two things the dispatcher needs beyond the alert text:
/// who should receive each alert type, and the stable identity of the underlying
/// condition.
/// </summary>
public static class NotificationAlertTypes
{
    public const string OverdueVaccination = "OverdueVaccination";
    public const string OverdueWeightCheck = "OverdueWeightCheck";
    public const string Medicine = "Medicine";
    public const string OverdueTask = "OverdueTask";
    public const string DueBirth = "DueBirth";
    public const string LowInventory = "LowInventory";

    /// <summary>
    /// A requested full-farm export finished building.
    ///
    /// <para>
    /// <b>Deliberately not in <see cref="All"/>.</b> <see cref="All"/> is the dashboard's
    /// alert vocabulary — conditions that exist or stop existing on the farm — and two
    /// things follow from it that this alert type must not be part of:
    /// </para>
    ///
    /// <list type="number">
    /// <item>the preferences matrix is built from <see cref="All"/>, and "email me when
    /// my export is ready" is not a farm condition anyone should have to configure;</item>
    /// <item><c>NotificationDispatcher</c> derives its auto-resolve set from
    /// <see cref="All"/> and resolves any open notification whose condition the dashboard
    /// no longer reports. A completion notice added to that set would be resolved as
    /// "no longer a problem" on the very next dispatch — the notice would silently
    /// disappear from the recipient's list minutes after arriving.</item>
    /// </list>
    ///
    /// <para>
    /// So the job writes this row directly, and <c>FarmExportNotificationTests</c> pins
    /// both halves of that: the type is absent from <see cref="All"/>, and a dispatch run
    /// does not touch it.
    /// </para>
    /// </summary>
    public const string ExportReady = "ExportReady";

    /// <summary>
    /// Where an export-ready notification points: the page that shows the archive and its
    /// download button. A client route rather than a URL, matching every other alert link.
    /// </summary>
    public const string ExportReadyLink = "/dashboard/configuration/export";

    /// <summary>Every alert type the dashboard can produce.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        OverdueVaccination,
        OverdueWeightCheck,
        Medicine,
        OverdueTask,
        DueBirth,
        LowInventory
    ];

    public static bool IsKnown(string? alertType) =>
        alertType is not null && All.Contains(alertType);

    /// <summary>
    /// The severity an alert type typically carries, used for the *default* email
    /// policy only.
    ///
    /// Channel defaults are decided per alert type, not per alert instance, so the
    /// preferences screen shows exactly what the dispatcher will do. A medicine
    /// alert that happens to be Critical therefore still follows the alert type's
    /// setting: "email me about medicines" is one decision, not one per batch.
    /// The instance severity still drives ordering and display.
    /// </summary>
    public static string DefaultSeverityFor(string alertType) => alertType switch
    {
        OverdueVaccination => NotificationSeverity.Critical,
        OverdueTask => NotificationSeverity.Critical,
        OverdueWeightCheck => NotificationSeverity.Warning,
        Medicine => NotificationSeverity.Warning,
        DueBirth => NotificationSeverity.Warning,
        LowInventory => NotificationSeverity.Info,
        _ => NotificationSeverity.Info
    };

    /// <summary>
    /// Roles that receive an alert type.
    ///
    /// <para>
    /// <see cref="LowInventory"/> mirrors <c>InventoryItemsController</c>'s
    /// <c>[Authorize(Roles = "Employee,Veterinarian,FarmManager,SystemOwner")]</c>:
    /// alerting somebody about a module they cannot open is noise, and an
    /// Accountant cannot open inventory items.
    /// </para>
    ///
    /// <para>
    /// The other five alert types are readable by any member, so their audience is
    /// every role except <see cref="FarmRoles.Viewer"/>. That exclusion is a
    /// deliberate policy rather than a mirror of the role attributes: Viewer is
    /// the read-only role, absent from every restricted module in the system, and
    /// notifications are prompts to act. Sending a Viewer every overdue vaccine on
    /// the farm is how a notification system gets ignored — the fatigue problem
    /// preferences also exist to prevent. Change this list (and
    /// <c>NotificationDispatchJobTests</c>) if Viewers should be alerted.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> GetAudience(string alertType) => alertType switch
    {
        LowInventory => InventoryModuleRoles,
        _ => MemberRoles
    };

    /// <summary>Roles that can act on an inventory alert (mirrors the controller's role attribute).</summary>
    public static readonly IReadOnlyList<string> InventoryModuleRoles =
    [
        FarmRoles.Employee,
        FarmRoles.Veterinarian,
        FarmRoles.FarmManager,
        FarmRoles.SystemOwner
    ];

    /// <summary>Every farm role except the read-only <see cref="FarmRoles.Viewer"/>.</summary>
    public static readonly IReadOnlyList<string> MemberRoles =
    [
        FarmRoles.SystemOwner,
        FarmRoles.FarmManager,
        FarmRoles.Veterinarian,
        FarmRoles.Employee,
        FarmRoles.Accountant
    ];

    /// <summary>
    /// The dashboard metric whose failure means the alert type could not be
    /// evaluated on this run.
    ///
    /// The dispatcher uses this to tell "there are no overdue vaccinations" apart
    /// from "the overdue-vaccination query failed". Without it a transient failure
    /// would look like the condition clearing, and the run would silently resolve
    /// live notifications.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MetricNameByAlertType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OverdueVaccination] = DashboardMetricNames.OverdueVaccinations,
            [OverdueWeightCheck] = DashboardMetricNames.OverdueWeightChecks,
            [Medicine] = DashboardMetricNames.MedicineAlerts,
            [OverdueTask] = DashboardMetricNames.OverdueTasks,
            [DueBirth] = DashboardMetricNames.DueBirthAlerts,
            [LowInventory] = DashboardMetricNames.LowInventory
        };

    /// <summary>Alert types that must not be reconciled when <paramref name="metricName"/> failed.</summary>
    public static IReadOnlyList<string> AlertTypesGatedByMetric(string metricName) =>
        MetricNameByAlertType
            .Where(pair => string.Equals(pair.Value, metricName, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();

    /// <summary>
    /// Identity of the underlying condition, built from entity ids only.
    ///
    /// Nothing date- or severity-shaped goes in: a due date moving forward is the
    /// same problem, not a new one, so it must update the existing notification
    /// rather than notify again.
    /// </summary>
    public static class SourceKeys
    {
        public static string ForOverdueVaccination(Guid animalId, Guid vaccineTypeId) =>
            $"{OverdueVaccination}:{animalId}:{vaccineTypeId}";

        public static string ForOverdueWeightCheck(Guid animalId) =>
            $"{OverdueWeightCheck}:{animalId}";

        public static string ForMedicine(Guid medicineId, Guid stockId, string medicineAlertType) =>
            $"{Medicine}:{medicineId}:{stockId}:{medicineAlertType}";

        public static string ForOverdueTask(Guid taskId) =>
            $"{OverdueTask}:{taskId}";

        public static string ForDueBirth(Guid gestationRecordId) =>
            $"{DueBirth}:{gestationRecordId}";

        public static string ForLowInventory(Guid inventoryItemId) =>
            $"{LowInventory}:{inventoryItemId}";

        /// <summary>
        /// One per <em>build</em>, so a farm that exports twice gets told twice.
        ///
        /// <para>
        /// This is the deliberate opposite of the rules above: the other keys are built
        /// from entity ids alone and deliberately exclude dates, because a condition that
        /// merely moves is the same problem and must not re-alert. An export is not a
        /// condition — it is an event — so two of them are two pieces of news, and the
        /// completion instant is what tells them apart. Without it both notices would
        /// share a key and anything keying on it would collapse them.
        /// </para>
        /// </summary>
        public static string ForExport(Guid exportId, DateTime completedAtUtc) =>
            $"{ExportReady}:{exportId}:{completedAtUtc:yyyyMMddHHmmssfff}";
    }
}

/// <summary>Severity ordering and the default channel policy derived from it.</summary>
public static class NotificationSeverity
{
    public const string Critical = "Critical";
    public const string Warning = "Warning";
    public const string Info = "Info";

    /// <summary>Lower is more severe; unknown severities sort last, matching the dashboard.</summary>
    public static int Rank(string? severity) => severity switch
    {
        Critical => 0,
        Warning => 1,
        Info => 2,
        _ => 3
    };

    /// <summary>
    /// Whether an alert of this severity is emailable by default.
    ///
    /// The default is the one that avoids both failure modes: emailing everything
    /// trains people to ignore the channel, while defaulting email off makes it
    /// invisible. Critical-only means the out-of-app channel carries the things
    /// worth interrupting somebody for, and anything quieter is opt-in per alert
    /// type on the preferences screen.
    /// </summary>
    public static bool IsEmailEnabledByDefault(string? severity, string minimumSeverity) =>
        Rank(severity) <= Rank(minimumSeverity);
}
