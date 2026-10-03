using System.Text.Json;
using FMS.Domain.Enums;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Keeps the server's <see cref="FarmOwnerAction"/> and the client's
/// <c>farmOwnerAction</c> vocabulary the same set of values.
/// <para>
/// The two live in different languages and neither can see the other. A value added to the enum
/// without a matching bundle entry would not throw anywhere: the client resolves the label,
/// finds nothing, and falls back to the raw wire value — so the refusal would read "Only farm
/// owners and managers can ArchiveMembers" inside a Spanish or Arabic interface, with the
/// English enum name showing through. Every other failure in this project has been a silent
/// fallback of some kind; this is the first one where the wrong thing to reach a reader is a
/// misspelt identifier.
/// </para>
/// <para>
/// The comparison is both ways. An entry in the vocabulary with no enum member is just as wrong
/// as the reverse — it is a translation no code can ever ask for, and it would sit in the bundle
/// looking maintained.
/// </para>
/// </summary>
public class FarmOwnerActionVocabularyTests
{
    /// <summary>
    /// The <c>farmOwnerAction</c> object from one locale's <c>enums.json</c>, as strings.
    /// <para>
    /// Read as raw JSON rather than through a deserialised type, because the point is to notice
    /// an entry the compiler knows nothing about.
    /// </para>
    /// <para>
    /// The values are copied out as <see cref="string"/> rather than returned as
    /// <see cref="JsonElement"/>. A <c>JsonElement</c> borrows from the document that owns it,
    /// so returning one past the <c>using</c> hands the caller a reference to freed memory — and
    /// the two tests that read labels failed that way while the one that reads only keys passed,
    /// which is exactly the sort of partial failure that looks like a data problem.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, string> Vocabulary(string locale)
    {
        var path = Path.Combine(ClientBundles.LocalesDirectory(), locale, "enums.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("farmOwnerAction", out var vocabulary))
        {
            throw new InvalidOperationException(
                $"{locale}/enums.json has no farmOwnerAction vocabulary. The unauthorized refusals "
                + "carry a FarmOwnerAction that is resolved through it.");
        }

        return vocabulary.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Every_farm_owner_action_has_a_label_in_every_locale()
    {
        // English first and separately: it is not a translation here, it is the sentence the API
        // has always returned, and a missing English label breaks the keyed path for every
        // language at once rather than degrading one of them.
        var actions = Enum.GetNames<FarmOwnerAction>().ToList();
        var problems = new List<string>();

        foreach (var locale in new[] { "en" }.Concat(ClientBundles.TranslatedLocales()).Distinct())
        {
            var vocabulary = Vocabulary(locale);
            foreach (var action in actions)
            {
                if (!vocabulary.TryGetValue(action, out var label))
                    problems.Add($"{locale}: no label for FarmOwnerAction.{action}");
                else if (string.IsNullOrWhiteSpace(label))
                    problems.Add($"{locale}: FarmOwnerAction.{action} has an empty label");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A FarmOwnerAction with no label renders as the raw enum name inside a translated "
            + "sentence. Add it to every locale's enums.json under farmOwnerAction:\n  "
            + string.Join("\n  ", problems));
    }

    [Fact]
    public void No_label_exists_for_an_action_the_server_cannot_send()
    {
        var actions = Enum.GetNames<FarmOwnerAction>().ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var locale in new[] { "en" }.Concat(ClientBundles.TranslatedLocales()).Distinct())
        {
            foreach (var name in Vocabulary(locale).Keys.Where(name => !actions.Contains(name)))
                problems.Add($"{locale}: farmOwnerAction.{name} has no FarmOwnerAction member");
        }

        Assert.True(
            problems.Count == 0,
            "These labels can never be reached, because the server has no value that would ask "
            + "for them. Remove them, or add the enum member they were written for:\n  "
            + string.Join("\n  ", problems));
    }

    [Fact]
    public void The_english_labels_reassemble_into_the_sentences_the_api_returns()
    {
        // The bundle holds "Only farm owners and managers can {{action}}", so the English label
        // is not a label at all — it is the end of a sentence the API has always returned, and
        // it has to fit. This asserts the two frames render exactly the eight refusals that were
        // there before, which is the rule the whole keyed mechanism rests on: English is
        // byte-identical, or every test asserting on a response body is asserting on the past.
        const string OwnersOrManagers = "Only farm owners and managers can ";
        const string Owners = "Only farm owners can ";

        var expected = new Dictionary<FarmOwnerAction, (string Frame, string Sentence)>
        {
            [FarmOwnerAction.ChangeMemberRoles] =
                (OwnersOrManagers, OwnersOrManagers + "change member roles"),
            [FarmOwnerAction.RemoveMembers] =
                (OwnersOrManagers, OwnersOrManagers + "remove members"),
            [FarmOwnerAction.InviteMembers] =
                (OwnersOrManagers, OwnersOrManagers + "invite members"),
            [FarmOwnerAction.ViewInvitations] =
                (OwnersOrManagers, OwnersOrManagers + "view invitations"),
            [FarmOwnerAction.RevokeInvitations] =
                (OwnersOrManagers, OwnersOrManagers + "revoke invitations"),
            [FarmOwnerAction.UpdateFarmDetails] =
                (OwnersOrManagers, OwnersOrManagers + "update farm details"),
            [FarmOwnerAction.DeleteFarms] =
                (Owners, Owners + "delete farms"),
        };

        var vocabulary = Vocabulary("en");
        var problems = new List<string>();

        foreach (var (action, (frame, sentence)) in expected)
        {
            if (!vocabulary.TryGetValue(action.ToString(), out var label))
            {
                problems.Add($"no English label for {action}");
                continue;
            }

            if (frame + label != sentence)
                problems.Add($"{frame}{{action}} with {action} renders \"{frame}{label}\","
                    + $" not \"{sentence}\"");
        }

        Assert.True(
            problems.Count == 0,
            "An English frame plus its action must reproduce the sentence the API has always "
            + "returned, exactly. English is not translated; it is preserved:\n  "
            + string.Join("\n  ", problems));
    }
}