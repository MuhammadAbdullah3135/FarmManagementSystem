namespace FMS.Domain.Tests.I18n;

/// <summary>
/// The server's own source files, for the guards that have to read code rather than run it.
/// <para>
/// Two of these guards exist because the gap they close is the <em>absence</em> of something —
/// a key, an argument dictionary — and nothing at runtime can tell an absent argument from a
/// present one. A source scan is the only thing that can see it, so both need the same walk,
/// and one implementation of it means a project added to the server tomorrow is scanned by
/// both without either test being edited.
/// </para>
/// </summary>
internal static class ServerSources
{
    /// <summary>The repository root, found by walking up from the test binary.</summary>
    public static string RepositoryRoot()
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
    /// The four projects that build domain messages.
    /// <para>
    /// Named rather than walked from <c>src/</c>: a recursive walk also descends into
    /// FMS.Mobile's <c>obj/</c>, which holds thousands of generated C# files and would make
    /// these tests slow for no coverage.
    /// </para>
    /// </summary>
    public static IEnumerable<string> SourceFiles() =>
        new[] { "FMS.API", "FMS.Application", "FMS.Domain", "FMS.Infrastructure" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot(), "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// A file's code with its comments removed.
    /// <para>
    /// These guards match on prose-shaped strings, and a sentence in a doc comment is not a
    /// call site — <c>LookupMessageKeyGuardTests</c> documents an example of a keyless refusal
    /// in a comment, which its own pattern would otherwise match. Stripping rather than
    /// skipping also means a commented-out call cannot be mistaken for a live one.
    /// </para>
    /// </summary>
    public static string WithoutComments(string source) =>
        System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/|(^|\s)//[^\n]*", "$1");

    /// <summary>A path as the guards print it: repository-relative and always forward-slashed.</summary>
    public static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot(), path).Replace(Path.DirectorySeparatorChar, '/');
}
