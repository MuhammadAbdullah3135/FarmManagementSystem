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
/// on a <c>Conflict</c> or <c>Superseded</c> refusal whose message is a <em>plain string
/// literal</em>. A sentence that takes an argument cannot be fully keyed until its caller passes
/// that argument along, and 22 such sentences (29 sites) are still to do — they are the rest of
/// slice 2 and are enumerated in <c>docs/I18N.md</c>. Excluding them by shape rather than by an
/// enumerated list of sentences means the guard blocks a new argument-free refusal today and
/// starts covering the argument-taking ones the moment they are keyed, with no edit here.
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
    /// </summary>
    private static readonly Regex Keyless = new(
        @"\.(?:Conflict|Superseded)\s*\(\s*""[^""]*""\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void No_argument_free_conflict_is_returned_without_a_key()
    {
        var offenders = new List<string>();
        foreach (var file in ServerSources.SourceFiles())
        {
            var code = ServerSources.WithoutComments(File.ReadAllText(file));

            var lines = code.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (Keyless.IsMatch(lines[index]))
                {
                    offenders.Add($"{ServerSources.Relative(file)}:{index + 1}  {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These refusals carry no i18n key, so they reach a Spanish or Arabic reader in "
            + "English. Add the matching DomainMessageKeys constant as the second argument (and "
            + "the key to the client bundles):\n  " + string.Join("\n  ", offenders));
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

        // Left alone: the argument-taking family, deferred to the rest of slice 2. An
        // interpolated message is a literal to a naive scanner, so this is the case that decides
        // whether the guard is usable at all.
        Assert.False(Keyless.IsMatch(
            "return Result.Conflict($\"A schedule at {FormatTime(timeOfDay)} already exists for this diet plan\");"));

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
