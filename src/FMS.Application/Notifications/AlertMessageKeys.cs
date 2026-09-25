using System.Text.Json;

namespace FMS.Application.Notifications;

/// <summary>
/// The message keys for the dashboard's alert templates, and the arguments each one needs.
///
/// <para>
/// An alert is *generated* on the server and *read* by a person, and those are two different
/// languages. Rather than pick one, every template is emitted twice: the English text it
/// has always carried (still used by the email digest, which has no client to hand a key
/// to, and as the client's fallback), plus a key and its arguments. The client renders the
/// key when it knows it and falls back to the text when it does not.
/// </para>
///
/// <para>
/// Arguments travel as raw values — a <see cref="DateTime"/> stays a date, a quantity stays
/// a number — never pre-formatted. The reader's locale decides how a date is written, and a
/// server that formatted "Aug 03, 2026" would have already made that decision for a Spanish
/// reader. Keys are spelled the way the client's resource file is nested:
/// <c>notifications.alertOverdueVaccinationTitle</c> is the flat key inside
/// <c>locales/*/notifications.json</c>.
/// </para>
/// </summary>
public static class AlertMessageKeys
{
    public const string OverdueVaccinationTitle = "notifications.alertOverdueVaccinationTitle";
    public const string OverdueVaccinationMessage = "notifications.alertOverdueVaccinationMessage";

    public const string OverdueWeightCheckTitle = "notifications.alertWeightCheckTitle";
    public const string OverdueWeightCheckMessage = "notifications.alertWeightCheckMessage";

    /// <summary>Same sentence with no last weight on record, so "Never" is a word, not an empty argument.</summary>
    public const string OverdueWeightCheckMessageNever = "notifications.alertWeightCheckMessageNever";

    /// <summary>
    /// One title per medicine alert kind. Three keys rather than an argument because the
    /// kind is a *word the reader sees* ("Expiring Soon"), and the dashboard's own alert
    /// type names are English constants — the key picks the translation, the argument
    /// cannot.
    /// </summary>
    public const string MedicineExpiredTitle = "notifications.alertMedicineExpiredTitle";
    public const string MedicineExpiringSoonTitle = "notifications.alertMedicineExpiringSoonTitle";
    public const string MedicineLowStockTitle = "notifications.alertMedicineLowStockTitle";

    /// <summary>Shared by all three medicine kinds: the batch sentence does not change with the kind.</summary>
    public const string MedicineMessage = "notifications.alertMedicineMessage";

    public const string OverdueTaskTitle = "notifications.alertOverdueTaskTitle";
    public const string OverdueTaskMessage = "notifications.alertOverdueTaskMessage";
    public const string OverdueTaskMessageAssigned = "notifications.alertOverdueTaskMessageAssigned";

    public const string DueBirthTitle = "notifications.alertDueBirthTitle";
    public const string DueBirthMessage = "notifications.alertDueBirthMessage";

    public const string LowInventoryTitle = "notifications.alertLowInventoryTitle";
    public const string LowInventoryMessage = "notifications.alertLowInventoryMessage";

    /// <summary>
    /// Builds an argument bag. Named rather than positional so a template's placeholders can
    /// be reordered in translation without touching this side.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Args(params (string Name, object? Value)[] pairs)
    {
        var args = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            args[name] = value;
        }

        return args;
    }
}

/// <summary>
/// Arguments as they are stored on a persisted notification.
///
/// <para>
/// The bag is a small, flat map of primitives, so it is kept as JSON in one column per
/// message rather than in a table of its own: it is read only to re-render the sentence,
/// never queried, and a condition's arguments change whenever the condition moves.
/// </para>
///
/// <para>
/// Deserialisation deliberately restores *primitives*. A default
/// <c>Dictionary&lt;string, object&gt;</c> would hand back <c>JsonElement</c> values, which the
/// client's renderer drops (it interpolates strings and numbers only) and which would
/// silently print nothing where a quantity or a date belongs.
/// </para>
/// </summary>
public static class MessageArgsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyDictionary<string, object?>? args) =>
        args is null || args.Count == 0 ? null : JsonSerializer.Serialize(args, Options);

    public static IReadOnlyDictionary<string, object?>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Options);
        if (raw is null) return null;

        var args = new Dictionary<string, object?>(raw.Count, StringComparer.Ordinal);
        foreach (var (name, element) in raw)
        {
            args[name] = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString(),
            };
        }

        return args;
    }
}
