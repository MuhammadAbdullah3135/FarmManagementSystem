using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Guards the wire, not the key: a controller that maps a domain failure must attach that
/// failure's i18n key to the response.
/// <para>
/// This is the check that was missing, and its absence is why 214 keyed messages shipped
/// inert. A service attaches a key to its <c>Error</c>; <see cref="FMS.API.ApiMessageKeys"/>
/// writes it onto the HTTP response as a header; the client prefers it and falls back to the
/// English text. Every link in that chain was tested except one. Thirty controllers mapped
/// failures with the same switch as the eight that worked, minus the single
/// <c>Attach</c> call — and nothing noticed, because the body, the status code and the whole
/// test suite were all correct. Only the header was missing, and a header nobody asserted is
/// a header nobody looks at.
/// </para>
/// <para>
/// It is the same failure as the silent-English fallback the key mechanism exists to
/// prevent, one layer earlier: the reader still gets English, and the reason is invisible.
/// So the rule is asserted here rather than trusted to review.
/// </para>
/// <para>
/// A source scan because the gap <em>is</em> the absence of a call. Nothing at runtime can
/// tell a mapper that attaches from one that does not, short of hosting the controller and
/// inspecting headers — which is what the post-deploy probe does, once, for the two
/// endpoints that bracket the difference.
/// </para>
/// </summary>
public class ControllerMessageKeyTests
{
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

    /// <summary>Comments describe this mapping in prose; none of them is a call.</summary>
    private static readonly Regex Comments = new(@"/\*[\s\S]*?\*/|(^|\s)//[^\n]*", RegexOptions.Compiled);

    /// <summary>A mapper that reads the failure — its code to pick a status, its text as a body.</summary>
    private static readonly Regex MapsADomainFailure = new(@"error\?\.(Code|Message)|error\.(Code|Message)", RegexOptions.Compiled);

    private static IEnumerable<string> ControllerSources()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "FMS.API", "Controllers");
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    [Fact]
    public void Every_controller_that_maps_a_domain_failure_attaches_its_key()
    {
        var offenders = new List<string>();

        foreach (var file in ControllerSources())
        {
            var code = Comments.Replace(File.ReadAllText(file), "$1");
            if (!MapsADomainFailure.IsMatch(code))
            {
                continue;
            }

            if (!code.Contains("ApiMessageKeys.Attach", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetFileName(file)} maps a domain failure but never calls "
                    + "ApiMessageKeys.Attach, so every key it serves is dropped before the "
                    + "response and the reader gets English.");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_rule_would_notice_a_mapper_that_forgets_to_attach()
    {
        // A guard that cannot fail reads as coverage, so the rule is exercised against both
        // shapes rather than only against the code it was written for.
        const string forgetting = @"
            private IActionResult MapError(Error error) => error.Code switch
            {
                ""NotFound"" => NotFound(error.Message),
                _ => StatusCode(500, error.Message)
            };";

        const string remembering = @"
            private IActionResult MapError(Error error)
            {
                ApiMessageKeys.Attach(Response, error);
                return error.Code switch
                {
                    ""NotFound"" => NotFound(error.Message),
                    _ => StatusCode(500, error.Message)
                };
            }";

        Assert.True(MapsADomainFailure.IsMatch(forgetting));
        Assert.False(forgetting.Contains("ApiMessageKeys.Attach", StringComparison.Ordinal));

        Assert.True(MapsADomainFailure.IsMatch(remembering));
        Assert.True(remembering.Contains("ApiMessageKeys.Attach", StringComparison.Ordinal));

        // A commented-out example is prose, not a mapper: stripping comments is what keeps
        // the first assertion honest.
        var commentedOut = Comments.Replace("// NotFound(error.Message)", "$1");
        Assert.False(MapsADomainFailure.IsMatch(commentedOut));
    }

    [Fact]
    public void The_shared_import_mapper_is_given_the_response_rather_than_reaching_for_it()
    {
        // ImportUpload.MapError is static and shared by seven import controllers, so it has no
        // `Response` of its own. Passing the response in is what keeps those seven honest; a
        // future caller that forgets it is invisible, so the call sites are counted instead.
        var file = Path.Combine(RepositoryRoot(), "src", "FMS.API", "Controllers", "ImportUpload.cs");
        var code = File.ReadAllText(file);

        Assert.Contains("ApiMessageKeys.Attach(response, error)", code, StringComparison.Ordinal);
        Assert.Contains("MapError(Error error, HttpResponse response)", code, StringComparison.Ordinal);

        var callers = ControllerSources()
            .Where(path => !path.EndsWith("ImportUpload.cs", StringComparison.Ordinal))
            .Select(path => new
            {
                File = Path.GetFileName(path),
                Uses = Regex.Matches(File.ReadAllText(path), @"ImportUpload\.MapError\(").Count,
                PassingResponse = Regex.Matches(
                    File.ReadAllText(path), @"ImportUpload\.MapError\([^;]*?,\s*Response\)").Count,
            })
            .Where(result => result.Uses > 0)
            .ToList();

        Assert.NotEmpty(callers);
        foreach (var caller in callers)
        {
            Assert.Equal(caller.Uses, caller.PassingResponse);
        }
    }
}
