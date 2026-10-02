using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Stops the "X not found" family from growing back into English.
/// <para>
/// 214 call sites across 22 services say some form of "… not found". They were all English
/// until this mission, and nothing stopped the next one from being added keyless: a key is
/// optional on <c>Error</c> and on <c>Result</c>, so a new refusal compiles, ships, and
/// silently degrades to English for every reader who did not choose English. That is how
/// 214 of them accumulated in the first place.
/// </para>
/// <para>
/// A source scan rather than a runtime one, because the gap is the absence of an argument
/// and nothing at runtime can tell an absent argument from a present one. The pattern is
/// deliberately narrow: a factory call whose <em>whole</em> first argument is a literal
/// containing "not found", immediately closed. A call that already passes a key cannot match,
/// because the key follows a comma. A call whose message is a variable cannot match either —
/// those carry their key in the variable (see <c>ParentRole</c> in AnimalService), and a
/// scanner that could not tell the two apart would fail on correct code.
/// </para>
/// <para>
/// The cost of the narrowness is stated rather than hidden: a keyless multi-line call, or one
/// whose message is built by concatenation, slips past. The parity test is the other half of
/// the guard — it cannot miss a key, only a missing key — so a message that reaches a reader
/// in English has to have escaped both.
/// </para>
/// </summary>
public class LookupMessageKeyGuardTests
{
    /// <summary>The repository root, found the same way the bundle tests find the client.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "FMS.API")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "The repository root was not found above the test binary; this test reads the sources.");
    }

    /// <summary>
    /// The four projects that build domain messages. Named rather than walked from
    /// <c>src/</c>: a recursive walk also descends into FMS.Mobile's <c>obj/</c>, which holds
    /// thousands of generated C# files and would make this test slow for no coverage.
    /// </summary>
    private static IEnumerable<string> SourceFiles()
    {
        var root = Path.Combine(RepositoryRoot(), "src");
        return new[] { "FMS.API", "FMS.Application", "FMS.Domain", "FMS.Infrastructure" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    [Fact]
    public void No_not_found_refusal_is_returned_without_a_key()
    {
        // A factory call whose first argument is a "… not found" literal and whose closing
        // paren follows immediately: that is exactly "no key was passed".
        var keyless = new Regex(
            @"\.(?:NotFound|Validation|Conflict|Unauthorized|Unexpected|Unavailable|Superseded|Failure)\s*\(\s*""[^""]*[Nn]ot found[^""]*""\s*\)",
            RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            // Comments describe these refusals in prose, and a sentence in a doc comment is
            // not a call site. Stripped rather than skipped, so a commented-out example
            // cannot be mistaken for a live one either.
            var code = Regex.Replace(File.ReadAllText(file), @"/\*[\s\S]*?\*/|(^|\s)//[^\n]*", "$1");

            var lines = code.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (keyless.IsMatch(lines[index]))
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(RepositoryRoot(), file).Replace(Path.DirectorySeparatorChar, '/')}:{index + 1}  {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These 'not found' refusals carry no i18n key, so they reach a Spanish or Arabic "
            + "reader in English. Add the matching DomainMessageKeys constant as the second "
            + "argument (and the key to the client bundles):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_guard_itself_would_notice_a_keyless_refusal()
    {
        // A guard that cannot fail is worse than none, because it reads as coverage. The
        // pattern is exercised against both shapes here: one that must match, and the keyed
        // form it must leave alone.
        var keyless = new Regex(
            @"\.(?:NotFound|Validation|Conflict|Unauthorized|Unexpected|Unavailable|Superseded|Failure)\s*\(\s*""[^""]*[Nn]ot found[^""]*""\s*\)",
            RegexOptions.Compiled);

        // The shape it must catch: a keyless refusal, on one line, in every factory the
        // domain uses for a failure.
        Assert.True(keyless.IsMatch("return Error.NotFound(\"Animal not found\");"));
        Assert.True(keyless.IsMatch("return Result.NotFound(\"Breed not found\");"));
        Assert.True(keyless.IsMatch("return Result<List<TaskDto>>.NotFound(\"Task not found\");"));
        Assert.True(keyless.IsMatch("return Result<PendingInvitationDto>.Unauthorized(\"User not found\");"));

        // Already keyed: a key follows the comma, so this must not match — otherwise the
        // guard would report the very sites it exists to protect.
        Assert.False(keyless.IsMatch(
            "return Error.NotFound(\"Animal not found\", DomainMessageKeys.AnimalNotFound);"));

        // A message in a variable carries its key in the variable; not this test's business.
        Assert.False(keyless.IsMatch("return Error.NotFound(role.NotFoundMessage, role.NotFoundKey);"));

        // Not this family at all.
        Assert.False(keyless.IsMatch("return Error.Conflict(\"An animal with tag '{tag}' already exists\");"));

        // A commented-out example is prose, not a call site. The guard strips comments
        // before matching, which is why this assertion is about the stripping rather than
        // about the pattern.
        Assert.False(keyless.IsMatch(Regex.Replace(
            "// return Error.NotFound(\"Animal not found\");", @"/\*[\s\S]*?\*/|(^|\s)//[^\n]*", "$1")));
    }
}
