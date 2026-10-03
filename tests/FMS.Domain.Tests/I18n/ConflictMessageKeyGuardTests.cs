using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Stops the conflict and supersede family from growing back into English.
/// <para>
/// This is the second half of slice 2. The first half keyed the 29 sentences that take no
/// argument, across 34 call sites in 14 services. A key is optional on <c>Error</c> and on
/// <c>Result</c>, so nothing stopped the next refusal being added keyless — which is exactly
/// how these 29 accumulated alongside the 214 "not found" ones that
/// <see cref="LookupMessageKeyGuardTests"/> now watches.
/// </para>
/// <para>
/// <b>The rule is deliberately narrow, and the narrowness is the design.</b> It requires a key
/// on a <c>Conflict</c> or <c>Superseded</c> refusal whose message is a string literal —
/// plain or interpolated. Both are literals to this scanner, and both must be keyed: an
/// interpolated sentence is the one a reader is <em>most</em> likely to meet in the wrong
/// language, because it names a value of theirs.
/// </para>
/// <para>
/// The interpolated form was excluded when 22 such sentences were still to do, and the
/// exclusion is now lifted because they are keyed: 21 sentences at 29 sites, each passing
/// its own <c>MessageArgs</c>. That is what the exclusion was buying — the guard could not
/// have required a key on a sentence whose caller had nowhere to put the arguments. Nothing
/// here enumerates those 21: they are covered because they took the keyed shape, and the
/// next argument-taking refusal is covered the moment it does the same.
/// </para>
/// <para>
/// A message held in a variable is also left alone, for the reason
/// <see cref="LookupMessageKeyGuardTests"/> gives: such a call carries its key in the variable
/// too, and a scanner that could not tell the two apart would fail on correct code.
/// </para>
/// </summary>
public class ConflictMessageKeyGuardTests
{
    /// <summary>
    /// A refusal that answers with a literal sentence and no key.
    /// <para>
    /// The closing paren follows immediately, which is exactly "no key was passed" — a keyed
    /// call cannot match, because the key follows a comma.
    /// </para>
    /// <para>
    /// The optional <c>$</c> is what widened this to the argument-taking family. It is the
    /// whole difference between the two forms: <c>"..."</c> and <c>$"..."</c> are the same
    /// call to this scanner except that the second one has holes in it, and a hole is
    /// precisely the reason a reader needs a translation.
    /// </para>
    /// </summary>
    /// <summary>
    /// A refusal that answers with a literal sentence and no key.
    /// <para>
    /// The closing paren follows immediately, which is exactly "no key was passed" — a keyed
    /// call cannot match, because the key follows a comma.
    /// </para>
    /// <para>
    /// The optional <c>$</c> is what widened this to the argument-taking family. It is the
    /// whole difference between the two forms: <c>"..."</c> and <c>$"..."</c> are the same
    /// call to this scanner except that the second one has holes in it, and a hole is
    /// precisely the reason a reader needs a translation.
    /// </para>
    /// <para>
    /// <b>This must be matched against the whole file, not line by line.</b> Every keyed call
    /// in this project puts its message on the line after <c>.Conflict(</c>, so a per-line
    /// match cannot see the two together and silently passes. That was caught by removing a
    /// key from <c>InventoryService</c> and watching the guard stay green — the fourth time
    /// in this project that a check reported success for a category it could not see. The
    /// <c>Singleline</c> option is what lets <c>\s*</c> span the newline; the line number in
    /// the failure message comes from the match offset.
    /// </para>
    /// <para>
    /// <see cref="ValidationMessageKeyGuardTests"/> copies this pattern for the validation
    /// family rather than sharing it, and the duplication is deliberate: the two guard
    /// different factories, and a shared abstraction over two regexes would be a place for
    /// both to be wrong at once. It also repeats the comments that matter — whole-file
    /// matching, the reported line, the named exemption — so a change to one is not read as a
    /// change to the other.
    /// </para>
    /// </summary>
    private static readonly Regex Keyless = new(
        @"\.(?:Conflict|Superseded)\s*\(\s*\$?""[^""]*""\s*\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Every keyless refusal in one file, as file-relative lines to report.
    /// <para>
    /// The line is where the match <em>starts</em>, and the text is the whole match with its
    /// newlines collapsed — so the report names both the file and the sentence that has to be
    /// keyed, which is the only thing anyone needs in order to fix it.
    /// </para>
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

    /// <summary>
    /// The one refusal this family still allows to be keyless, named rather than patterned.
    /// <para>
    /// <c>ConfigurationService.InUse</c> builds <c>Cannot delete {entityLabel} '{name}': still
    /// used by {usedBy}. {remedy}</c> from three fragments the seven call sites already own,
    /// and <c>usedBy</c> is itself an English <c>string.Join</c> with a plural on each label. A
    /// key cannot fix that: it would ship a sentence whose <c>{usedBy}</c> is still English,
    /// which is the same defect one layer down. It needs structured arguments, i18next plurals
    /// and a translated list joiner, and it is tracked as such in <c>docs/I18N.md</c>.
    /// </para>
    /// <para>
    /// It is named here rather than excluded by shape because it is the only such sentence
    /// and the shape that would exclude it — "an interpolated literal" — is now the whole
    /// point of the guard. A shape-based exemption would have undone slice 2 to accommodate
    /// the one sentence slice 2 could not cover. Naming it also means it is visible: this
    /// list is one line long, and adding to it is a deliberate act someone has to write.
    /// </para>
    /// </summary>
    private static readonly string[] AllowedKeyless =
    {
        "src/FMS.Infrastructure/Configuration/ConfigurationService.cs",
    };

    [Fact]
    public void No_conflict_is_returned_without_a_key()
    {
        var offenders = new List<string>();
        foreach (var file in ServerSources.SourceFiles())
        {
            var relative = ServerSources.Relative(file);
            var code = ServerSources.WithoutComments(File.ReadAllText(file));

            var found = Offenders(relative, code);
            // An allowed file is skipped only where it actually matches, so a *second* keyless
            // refusal appearing alongside the known one is still caught.
            if (AllowedKeyless.Contains(relative)) continue;
            offenders.AddRange(found);
        }

        Assert.True(
            offenders.Count == 0,
            "These refusals carry no i18n key, so they reach a Spanish or Arabic reader in "
            + "English. Add the matching DomainMessageKeys constant as the second argument (and "
            + "the key to the client bundles):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_one_allowed_refusal_is_still_only_one()
    {
        // An exemption with no ceiling is a hole with a comment on it. `InUse` is a single
        // factory for seven call sites, so one entry is one sentence; if that sentence ever
        // becomes keyed this list goes to empty, and until then it stays this short.
        //
        // The list is checked against the file itself rather than trusted, so removing the
        // exemption fails here instead of silently re-allowing the composite — and so a
        // second keyless refusal in that same file fails here too.
        var allowed = ServerSources.SourceFiles()
            .Where(file => AllowedKeyless.Contains(ServerSources.Relative(file)))
            .SelectMany(file => Offenders(ServerSources.Relative(file),
                ServerSources.WithoutComments(File.ReadAllText(file))))
            .ToList();

        Assert.Equal(
            allowed,
            new[]
            {
                "src/FMS.Infrastructure/Configuration/ConfigurationService.cs:35  "
                    + ".Conflict($\"Cannot delete {entityLabel} '{name}': still used by {usedBy}. {remedy}\")",
            }.ToList());
    }

    [Fact]
    public void The_guard_itself_would_notice_a_keyless_refusal()
    {
        // A guard that cannot fail is worse than none, because it reads as coverage. Every shape
        // it must catch and every shape it must leave alone is exercised here.

        // Caught: a plain refusal, in both factories the domain uses for "this already happened".
        Assert.True(Keyless.IsMatch("return Error.Conflict(\"Email already registered\");"));
        Assert.True(Keyless.IsMatch("return Result<AttendanceRecordDto>.Conflict(\"Email already registered\");"));
        Assert.True(Keyless.IsMatch("return Error.Superseded(\"This completion was already applied\");"));
        Assert.True(Keyless.IsMatch("return Result<PendingInvitationDto>.Superseded(\"Task is already completed\");"));

        // Left alone: already keyed, because the key follows the comma. This is the form all 34
        // slice-2 sites now take.
        Assert.False(Keyless.IsMatch(
            "return Error.Conflict(\"Email already registered\", DomainMessageKeys.EmailAlreadyRegistered);"));
        Assert.False(Keyless.IsMatch(
            "return Result<AttendanceRecordDto>.Superseded(\"Task is already completed\", DomainMessageKeys.TaskAlreadyCompleted);"));

        // Caught: the argument-taking family, which is 20 sentences at 28 sites now that
        // slice 2 is finished. An interpolated message is a literal to a naive scanner, so
        // this is the case that decides whether the guard is usable at all.
        Assert.True(Keyless.IsMatch(
            "return Result.Conflict($\"A schedule at {FormatTime(timeOfDay)} already exists for this diet plan\");"));
        Assert.True(Keyless.IsMatch(
            "return Result<FeedRecordDto>.Conflict($\"Insufficient stock: current stock is {currentStock} {feedType.Unit}\");"));
        Assert.True(Keyless.IsMatch(
            "return Error.Conflict($\"An animal with tag '{tag}' already exists in this farm\");"));
        Assert.True(Keyless.IsMatch(
            "return Result<AttendanceRecordDto>.Superseded($\"That day already has an attendance record marked {existing.Status}\");"));

        // Caught across a line break, which is how every keyed call in this project is
        // written and therefore how a new one will arrive. A per-line scan cannot see this:
        // it matches a single line, and this call's opening paren and its sentence are on
        // different lines, so a per-line match never sees them together.
        var acrossLines = "return Result<StockMovementDto>.Conflict(\n"
            + "    $\"Insufficient stock: {item.Quantity}\");";
        Assert.True(
            Keyless.IsMatch(acrossLines),
            "the pattern did not match: " + acrossLines.Replace("\n", "\\n"));
        // The reported line is where the match *starts*, which is the `.Conflict(` — so this
        // is line 1 even though the sentence is on line 2.
        Assert.Equal(
            "f.cs:1  .Conflict( $\"Insufficient stock: {item.Quantity}\")",
            Offenders("f.cs", "return Result.Conflict(\n"
                + "    $\"Insufficient stock: {item.Quantity}\");").Single());

        // Left alone: the same sentences once keyed, which is the form all 29 slice-2b sites
        // take. The arguments follow the key, so the closing paren is nowhere near.
        Assert.False(Keyless.IsMatch(
            "return Error.Conflict($\"An animal with tag '{tag}' already exists in this farm\", DomainMessageKeys.AnimalTagExists, new Dictionary<string, object?> { [\"tag\"] = tag });"));

        // Left alone: a message in a variable, which carries its key in the variable.
        Assert.False(Keyless.IsMatch("return Error.Conflict(rule.ConflictMessage);"));

        // Not this family's factories at all. A factory whose name merely ends in "Conflict" is
        // not one of them, which is why the pattern names the two exactly.
        Assert.False(Keyless.IsMatch("return Error.NotFoundConflict(\"Email already registered\");"));
        Assert.False(Keyless.IsMatch("return Error.NotFound(\"Animal not found\");"));
        Assert.False(Keyless.IsMatch("return Error.Validation(\"First name is required\");"));

        // A commented-out example is prose, not a call site.
        Assert.False(Keyless.IsMatch(ServerSources.WithoutComments(
            "// return Error.Conflict(\"Email already registered\");")));
    }
}
