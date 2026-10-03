using System.Reflection;
using FMS.Application.Common;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Phase 7.4's server half, for the domain refusals: every <see cref="DomainMessageKeys"/>
/// constant is a key the client can actually render, in every locale the client ships.
/// <para>
/// The join between the two halves, and until this mission nothing tested it for these keys.
/// The server states what happened and the client decides how to say it, so a typo or a
/// one-sided addition does not throw — the client silently falls back to the English text the
/// server also sent. Silent English inside a translated UI is the failure this whole
/// mechanism exists to remove, so it is asserted here.
/// </para>
/// <para>
/// Reflection rather than a hand-written list, so a constant added tomorrow is covered the
/// moment it exists. Locales are discovered from disk rather than listed, because 7.3 added
/// Arabic and a hard-coded list would go on checking Spanish while Arabic went unchecked.
/// </para>
/// <para>
/// English matters as much as the translations: a key missing from <c>en</c> would disable
/// the keyed path for every language at once.
/// </para>
/// </summary>
public class DomainMessageKeyParityTests
{
    /// <summary>Every <c>const string</c> the domain declares as a message key.</summary>
    private static List<string> DeclaredKeys() =>
        typeof(DomainMessageKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.Contains('.'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_domain_message_key_exists_in_every_client_bundle()
    {
        var keys = DeclaredKeys();

        // A reflection change that silently enumerated nothing would make this test pass
        // for the wrong reason, which is worse than not having it. So the check is a floor, not
        // an equality: the total can only grow as families are keyed, and the lookup family
        // itself is pinned at its own count so a reflection change that dropped *one* group
        // while keeping another would still be caught.
        // 64 lookup + 47 conflict + 3 supersede + 69 validation = 183.
        Assert.True(
            keys.Count >= 183,
            $"Only {keys.Count} domain message keys were found; at least 183 are expected. A "
            + "reflection change that enumerates fewer keys than the class declares would make "
            + "this test pass for the wrong reason.");
        Assert.Equal(64, keys.Count(key => key.StartsWith("validation.lookup.", StringComparison.Ordinal)));
        // The conflict and supersede families separately, so neither can be emptied by a
        // reflection change that still leaves the other one populated. 47 is 27 argument-free
        // plus 20 argument-taking; the supersede family is 2 argument-free plus 1.
        Assert.Equal(47, keys.Count(key => key.StartsWith("validation.conflict.", StringComparison.Ordinal)));
        Assert.Equal(3, keys.Count(key => key.StartsWith("validation.supersede.", StringComparison.Ordinal)));
        // The validation family is pinned for the same reason, and is the one still growing:
        // 16 argument-taking sentences, then 23 argument-free across FeedService and
        // AnimalService. Each service's sweep raises this number, which is the point of pinning
        // it — a reflection change that missed the newest group would otherwise go unnoticed.
        Assert.Equal(69, keys.Count(key => key.StartsWith("validation.validation.", StringComparison.Ordinal)));

        // One bundle read per namespace, not per key: the lookup family is one namespace.
        var bundles = ClientBundles.TranslatedLocales()
            .Append("en")
            .Distinct()
            .ToDictionary(locale => locale, locale => ClientBundles.Bundle(locale, "validation"));

        foreach (var key in keys)
        {
            var (namespaceName, path) = ClientBundles.SplitKey(key);
            Assert.Equal("validation", namespaceName);

            foreach (var (locale, bundle) in bundles)
            {
                Assert.True(bundle.ContainsKey(path), $"{key} is missing from {locale}/validation.json");
                Assert.False(
                    string.IsNullOrWhiteSpace(bundle[path]),
                    $"{key} is blank in {locale}/validation.json");
            }
        }
    }    [Fact]
    public void Every_domain_key_lives_in_a_group_the_client_also_uses()
    {
        // The keys name the group they live in twice over: the constant says
        // `validation.lookup.animal` and the bundle nests it under `lookup`. A constant that
        // drifted to another group would still resolve, but the two sides would no longer
        // describe the same place — which is the kind of drift nobody notices until a
        // translator goes looking for the string.
        //
        // The groups are the four the domain families occupy: `lookup` for a record that is not
        // there, `conflict` for one that already is, `supersede` for one this request was
        // answering with a newer version of, and `validation` for a request the server
        // understood and refused on its own terms — a missing column mapping, a file that is
        // too large, an import field that points past the end of the sheet.
        //
        // The fourth family was added when the argument-taking validation sentences ran out of
        // homes. `lookup` means "no such record", which is a specific and different claim from
        // "this value is not allowed", and filing them together would have taught a
        // translator that the group tells them nothing. The list extends as families are added,
        // which is the point — a key that names a group nobody bundles cannot render, and it
        // cannot fail at runtime either.
        string[] groups = { "lookup.", "conflict.", "supersede.", "validation." };

        var bundle = ClientBundles.Bundle("en", "validation");
        foreach (var key in DeclaredKeys())
        {
            var (_, path) = ClientBundles.SplitKey(key);

            Assert.True(
                groups.Any(group => path.StartsWith(group, StringComparison.Ordinal)),
                $"{key} names a group the client's validation bundle does not have; the server "
                + "and the bundle have to agree on where the key lives.");
        }

        // And the reverse: nothing sits in one of those groups in the client's bundle that the
        // server cannot name, so a key added to the bundle alone is caught rather than waiting
        // for a call site that will never come.
        var declared = DeclaredKeys()
            .Select(key => ClientBundles.SplitKey(key).Path)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = bundle.Keys
            .Where(path => groups.Any(group => path.StartsWith(group, StringComparison.Ordinal)))
            .Where(path => !declared.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphans);
    }
}
