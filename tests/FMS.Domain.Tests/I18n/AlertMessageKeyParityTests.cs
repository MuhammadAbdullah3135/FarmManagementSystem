using System.Reflection;
using System.Text.Json;
using FMS.Application.Notifications;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Phase 7.2's server half: the keys the server sends are keys the client can actually render.
///
/// <para>
/// This is the join between the two halves of the design, and until now nothing tested it. The
/// server states *what* happened (`notifications.alertOverdueVaccinationTitle`) and the client
/// decides *how* to say it; a typo, or a key added on one side only, therefore does not throw —
/// the client silently falls back to the English text the server also sent. Silent English in a
/// translated UI is exactly the failure this subphase exists to remove, so it is asserted here
/// instead: for every alert template the server can emit, the key exists in *every* bundle.
/// </para>
///
/// <para>
/// Every locale the client ships, discovered from the resource directory rather than listed
/// here: 7.3 added Arabic, and a hand-written list would have gone on checking Spanish while
/// the Arabic bundle was the one missing keys.
/// </para>
///
/// <para>
/// The English side matters as much as the Spanish one. `canRenderMessageKey` asks the English
/// resources whether the key is known, so a key missing from English would disable the keyed
/// path for every language at once.
/// </para>
///
/// <para>
/// Reflection rather than a hand-written list: a template added later is covered by this test
/// the moment its constant exists, which is the only way a check like this stays true.
/// </para>
/// </summary>
public class AlertMessageKeyParityTests
{
    // The bundle-reading and key-enumerating helpers now live in ClientBundles, shared with
    // DomainMessageKeyParityTests. They used to be private here, which meant a second copy of
    // the directory walk to forget when a language was added.

    private static List<string> DeclaredKeys() => ClientBundles.DeclaredKeys(typeof(AlertMessageKeys));

    [Fact]
    public void Every_alert_template_key_exists_in_every_client_bundle()
    {
        var keys = DeclaredKeys();
        Assert.NotEmpty(keys);

        var english = ClientBundles.Bundle("en", "notifications");

        foreach (var key in keys)
        {
            // `notifications.alertOverdueVaccinationTitle` names the flat key inside the
            // notifications namespace — the client splits on the first dot for exactly this.
            var (namespaceName, path) = ClientBundles.SplitKey(key);
            Assert.Equal("notifications", namespaceName);

            Assert.True(english.ContainsKey(path), $"{key} is missing from en/notifications.json");

            foreach (var locale in ClientBundles.TranslatedLocales())
            {
                var bundle = ClientBundles.Bundle(locale, "notifications");
                Assert.True(bundle.ContainsKey(path), $"{key} is missing from {locale}/notifications.json");
                Assert.False(string.IsNullOrWhiteSpace(bundle[path]), $"{key} is blank in {locale}");
            }
        }
    }

    [Fact]
    public void Every_alert_type_the_server_stores_has_a_label_key_in_every_bundle()
    {
        // The client translates an alert's *type* through its own vocabulary table, keyed by the
        // value the server stores (`OverdueVaccination` → `alertTypeOverdueVaccination`). A type
        // with no label would show the raw stored value in the middle of a translated sentence.
        var types = typeof(NotificationAlertTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            // Excluded: the client route an alert points at, and the source-key *patterns*
            // (`OverdueVaccination:{animalId}`), which are identities rather than types.
            .Where(value => !value.StartsWith('/') && !value.Contains(':') && !value.Contains('{'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(types);

        var english = ClientBundles.Bundle("en", "notifications");

        foreach (var type in types)
        {
            var key = $"alertType{type}";
            Assert.True(english.ContainsKey(key), $"{key} is missing from en/notifications.json");

            foreach (var locale in ClientBundles.TranslatedLocales())
            {
                Assert.True(
                    ClientBundles.Bundle(locale, "notifications").ContainsKey(key),
                    $"{key} is missing from {locale}/notifications.json");
            }
        }
    }

    [Fact]
    public void The_server_locale_list_and_the_client_bundles_name_the_same_languages()
    {
        // The two halves are kept in step by hand (`SupportedLocales.All` and the resource
        // directories), so this is the check that notices when one gains a language the other
        // does not have: storing a language the client cannot render is a silent English page.
        var clientLocales = ClientBundles.TranslatedLocales();
        var serverLocales = FMS.Application.Common.SupportedLocales.All
            .Where(locale => locale != FMS.Application.Common.SupportedLocales.Default)
            .OrderBy(locale => locale, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(clientLocales, serverLocales);
    }

    [Fact]
    public void Argument_bags_round_trip_as_primitives_the_client_can_interpolate()
    {
        // The client's renderer interpolates strings and numbers and drops anything else, so a
        // `JsonElement` coming back out of the column would print nothing where a quantity or a
        // date belongs. Dates deliberately travel as ISO strings: the reader's locale formats
        // them, and a server that formatted them would have decided for a Spanish reader.
        var args = AlertMessageKeys.Args(
            ("tag", "COW-001"),
            ("quantity", 12),
            ("ratio", 2.5),
            ("expired", true),
            ("employee", null),
            ("dueDate", new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc)));

        var restored = MessageArgsJson.Deserialize(MessageArgsJson.Serialize(args));

        Assert.NotNull(restored);
        Assert.Equal("COW-001", restored!["tag"]);
        Assert.Equal(12L, restored["quantity"]);
        Assert.Equal(2.5, restored["ratio"]);
        Assert.Equal(true, restored["expired"]);
        Assert.Null(restored["employee"]);
        // An ISO-8601 string, which is what the client's date formatter recognises.
        Assert.StartsWith("2026-09-09", (string)restored["dueDate"]!);

        // No JsonElement survived the trip.
        Assert.All(restored.Values, value =>
            Assert.False(value is JsonElement, "a JsonElement would interpolate as nothing on the client"));
    }

    [Fact]
    public void An_empty_or_absent_bag_is_stored_as_no_json_at_all()
    {
        // Null rather than "{}": the column is only read to re-render a sentence, and an empty
        // object would claim there is something to render.
        Assert.Null(MessageArgsJson.Serialize(null));
        Assert.Null(MessageArgsJson.Serialize(new Dictionary<string, object?>()));
        Assert.Null(MessageArgsJson.Deserialize(null));
        Assert.Null(MessageArgsJson.Deserialize("  "));
    }
}
