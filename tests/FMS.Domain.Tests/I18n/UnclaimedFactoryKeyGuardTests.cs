using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Watches the four result factories no other guard claims.
/// <para>
/// <c>Validation</c> and <c>Failure</c> belong to <see cref="ValidationMessageKeyGuardTests"/>,
/// <c>Conflict</c> and <c>Superseded</c> to <see cref="ConflictMessageKeyGuardTests"/>, and
/// <c>NotFound</c> to <see cref="LookupMessageKeyGuardTests"/> — but that last one watches
/// <em>the words</em> “not found”, wherever they appear, rather than the factory. So a
/// <c>NotFound</c> call whose sentence merely fails to contain that phrase was invisible to it:
/// “No attendance record found for today”, “This farm has no export archive yet”, “That user is
/// not a member of this farm”. Five of the seven sentences it should have caught did not say
/// “not found”, and neither did any of the <c>Unauthorized</c>, <c>Unexpected</c> or
/// <c>Unavailable</c> ones, because those two guards do not claim those factories at all.
/// </para>
/// <para>
/// The lesson is the one this file keeps earning. A guard that keys on the <em>text</em> of a
/// message rather than on the <em>code</em> that produces it is guarding the sentences someone
/// happened to write a certain way. The two guards above are kept as well, and each catches
/// something this one cannot: this one sees every keyless call to these four factories whatever
/// it says, and the content-based one sees a “not found” refusal that was filed under the wrong
/// factory, which is a different mistake with the same symptom.
/// </para>
/// <para>
/// The pattern is duplicated from the other guards rather than shared, deliberately — a helper
/// over four regexes is a place where all of them could be wrong together, and the comments that
/// matter are repeated instead so editing one guard does not read as editing the others.
/// </para>
/// <para>
/// This one carries no exemption list, and that is the point of writing it last: the seven
/// sentences it found were the whole remainder, so the assertion can be outright. The one
/// sentence still unkeyed in the domain belongs to a different factory and is exempt in
/// <see cref="ConflictMessageKeyGuardTests"/>, where the decision and its reason live.
/// </para>
/// </summary>
public class UnclaimedFactoryKeyGuardTests
{
    /// <summary>
    /// A refusal whose factory this guard claims, carrying a literal sentence and no key.
    /// <para>
    /// The closing paren follows the last literal, which is exactly “no key was passed” — a
    /// keyed call cannot match, because the key follows a comma.
    /// </para>
    /// <para>
    /// Adjacent literals joined with <c>+</c> are matched as one sentence, and that is not
    /// tidiness. The first version of this pattern stopped at the first closing quote, and the
    /// one <c>Unavailable</c> refusal in the domain — a three-part sentence about disabled
    /// background jobs — is written that way, so the guard could not see the single site it was
    /// written for. A guard with a hole the size of its own subject is the failure this project
    /// keeps meeting, and this one would have shipped it on the day it was added.
    /// </para>
    /// <para>
    /// <b>Matched against the whole file, not line by line.</b> Every keyed call in this project
    /// puts its message on the line after <c>.NotFound(</c>, so a per-line scan cannot see the two
    /// together and passes silently. The <c>Singleline</c> option is not what makes that work —
    /// <c>\s</c> already matches a newline — but it is kept for symmetry with the other guards.
    /// </para>
    /// </summary>
    private static readonly Regex Keyless = new(
        @"\.(?:NotFound|Unauthorized|Unexpected|Unavailable)\s*\(\s*\$?""[^""]*""(?:\s*\+\s*\$?""[^""]*""\s*)*\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Every keyless refusal to one of those factories in one file, as file-relative lines.
    /// </summary>
    private static List<string> Offenders(string relative, string code)
    {
        var found = new List<string>();
        for (var match = Keyless.Match(code); match.Success; match = match.NextMatch())
        {
            var line = code.Take(match.Index).Count(ch => ch == '\n') + 1;
            var text = string.Join(' ', match.Value.Split('\n').Select(part => part.Trim()));
            found.Add($"{relative}:{line}  {text}");
        }

        return found;
    }

    [Fact]
    public void No_refusal_from_an_unclaimed_factory_is_returned_without_a_key()
    {
        // No exemption list, which is the state every other guard in this folder is working
        // towards. The first version of this test carried one for ConfigurationService, on the
        // assumption that the domain's one remaining unkeyed sentence lived in a factory this
        // guard claimed. It does not: `Cannot delete {entityLabel} '{name}': still used by …` is
        // a `Conflict`, so it was already named as an exemption by ConflictMessageKeyGuardTests
        // and this guard never opened the file. The list was a copy of somebody else's decision,
        // and the ratchet test that watched it failed immediately — which is what it was for.
        //
        // So the exemption lives in the guard that owns the factory, and this one asserts
        // outright. If the InUse composite ever becomes a key, nothing here changes; if a new
        // refusal appears in one of these four factories, this is what fails.
        var offenders = new List<string>();
        foreach (var file in ServerSources.SourceFiles())
        {
            offenders.AddRange(Offenders(
                ServerSources.Relative(file),
                ServerSources.WithoutComments(File.ReadAllText(file))));
        }

        Assert.True(
            offenders.Count == 0,
            "These refusals reach a Spanish or Arabic reader in English. Add the matching "
            + "DomainMessageKeys constant as the second argument (and the key to the client "
            + "bundles):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_guard_itself_would_notice_a_keyless_refusal()
    {
        // A guard that cannot fail is worse than none, because it reads as coverage. Every shape
        // it must catch and every shape it must leave alone is exercised here.

        // Caught: each of the four factories it claims, in the form the domain writes them.
        Assert.True(Keyless.IsMatch("return Result<MedicineDto>.NotFound(\"Medicine not found\");"));
        Assert.True(Keyless.IsMatch("return Result<FarmDto>.Unauthorized(\"Only farm owners can do that\");"));
        Assert.True(Keyless.IsMatch("return Error.Unexpected(\"Failed to store file\");"));
        Assert.True(Keyless.IsMatch("return Result.Unavailable(\"Background jobs are disabled\");"));

        // Caught: the five sentences that the word-based lookup guard could not see, because none
        // of them happens to contain the words "not found". This is the case the guard exists for.
        Assert.True(Keyless.IsMatch("return Result<...>.NotFound(\"No attendance record found for today\");"));
        Assert.True(Keyless.IsMatch("return Result<...>.NotFound(\"This farm has no export archive yet.\");"));
        Assert.True(Keyless.IsMatch("return Result<...>.NotFound(\"That user is not a member of this farm\");"));
        Assert.True(Keyless.IsMatch("return Result<...>.NotFound(\"The export archive is no longer stored.\");"));

        // Caught: adjacent literals joined into one sentence, which is how the domain's only
        // Unavailable refusal is written, and which the first version of this pattern missed.
        Assert.True(Keyless.IsMatch(
            "return Result<X>.Failure(Error.Unavailable(\n"
            + "    \"First part of the sentence \"\n"
            + "    + \"and the rest of it.\"));"));
        Assert.True(Keyless.IsMatch("return Error.NotFound(\"one \" + \"two\");"));

        // Caught across a line break, which is how every keyed call in this project is written.
        Assert.True(Keyless.IsMatch(
            "return Result<FarmDto>.NotFound(\n    \"Farm not found or access denied\");"));
        Assert.Equal(
            "f.cs:1  .NotFound( \"Farm not found or access denied\")",
            Offenders("f.cs", "return Result<FarmDto>.NotFound(\n    \"Farm not found or access denied\");").Single());

        // Caught: an interpolated sentence, which is the one a reader is most likely to meet in
        // the wrong language because it names a value of theirs.
        Assert.True(Keyless.IsMatch("return Error.NotFound($\"No record of {kind} was found\");"));

        // Left alone: already keyed, because the key follows the comma.
        Assert.False(Keyless.IsMatch(
            "return Result<MedicineDto>.NotFound(\"Medicine not found\", DomainMessageKeys.MedicineNotFound);"));
        Assert.False(Keyless.IsMatch(
            "return Result<FarmDto>.Unauthorized(\n"
            + "    \"Only farm owners can delete farms\",\n"
            + "    DomainMessageKeys.FarmOwnerOnly,\n"
            + "    new Dictionary<string, object?> { [\"action\"] = FarmOwnerAction.DeleteFarms.ToString() });"));

        // Left alone: a message in a variable, which carries its key in the variable.
        Assert.False(Keyless.IsMatch("return Error.NotFound(rule.NotFoundMessage);"));

        // Left alone: factories another guard claims. This one must not double-report a
        // validation or a conflict, or its output stops being a list of what is *unwatched*.
        Assert.False(Keyless.IsMatch("return Error.Validation(\"First name is required\");"));
        Assert.False(Keyless.IsMatch("return Result.Validation(\n    \"Quantity must be greater than zero\");"));
        Assert.False(Keyless.IsMatch("return Error.Conflict(\"Already exists\");"));
        Assert.False(Keyless.IsMatch("return Result.Conflict(\"A plan is already active\");"));
        Assert.False(Keyless.IsMatch("return Result.Superseded(\"Already applied\");"));

        // A commented-out example is prose, not a call site.
        Assert.False(Keyless.IsMatch(ServerSources.WithoutComments(
            "// return Error.NotFound(\"Medicine not found\");")));
    }
}