using System.Reflection;
using System.Text.RegularExpressions;
using FMS.API;
using FMS.Application.Common;
using FMS.Application.Notifications;
using Microsoft.AspNetCore.Http;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// A keyed sentence that takes arguments has to arrive with them.
/// <para>
/// The 7.4 mission keyed 249 call sites and every one of them that emits a placeholder key
/// computes its arguments correctly — this class's sibling assertion, below, is what proves
/// that. <c>ApiMessageKeys.Attach</c> then wrote the key to a header and dropped
/// <c>Error.MessageArgs</c> on the floor, so the client, which prefers the key over the
/// English, rendered "First name cannot exceed {{max}} characters": a broken sentence, in the
/// reader's own language, where the correct English had been sitting in the body all along.
/// Arguments had only ever travelled by body, on the import preview's per-row problems, which
/// is why every test that exercised the header path passed while the feature was broken in
/// production.
/// </para>
/// <para>
/// Probed on v48 before the fix: <c>POST /farm/{id}/employees</c> with a 120-character first
/// name answered 400 with <c>x-message-key: validation.employee.firstNameMaxLength</c>, no
/// second header, and the body "First name cannot exceed 100 characters".
/// </para>
/// </summary>
public class MessageArgsHeaderTests
{
    [Fact]
    public void A_key_with_arguments_writes_both_headers()
    {
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Validation(
            "First name cannot exceed 100 characters",
            "validation.employee.firstNameMaxLength",
            AlertMessageKeys.Args(("max", 100))));

        Assert.Equal(
            "validation.employee.firstNameMaxLength",
            context.Response.Headers[ApiMessageKeys.HeaderName].ToString());
        Assert.True(context.Response.Headers.ContainsKey(ApiMessageKeys.ArgsHeaderName));
    }

    [Fact]
    public void A_key_with_no_arguments_writes_only_the_key()
    {
        // Most of the family: "Email already registered" takes nothing, and adding an empty
        // second header for it would be noise on every such response.
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Conflict("Email already registered", "validation.auth.emailRegistered"));

        Assert.Equal("validation.auth.emailRegistered", context.Response.Headers[ApiMessageKeys.HeaderName].ToString());
        Assert.False(context.Response.Headers.ContainsKey(ApiMessageKeys.ArgsHeaderName));
    }

    [Fact]
    public void A_keyless_failure_writes_no_headers_at_all()
    {
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Validation("No key on this one"));

        Assert.False(context.Response.Headers.ContainsKey(ApiMessageKeys.HeaderName));
        Assert.False(context.Response.Headers.ContainsKey(ApiMessageKeys.ArgsHeaderName));
    }

    [Fact]
    public void The_arguments_survive_the_round_trip()
    {
        // The client's parse is a hand-written decodeURIComponent plus JSON.parse, and this
        // is what pins it against the encoding this class actually emits rather than against
        // a description of it. A change to either side that broke the other would fail here.
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Validation(
            "First name cannot exceed 100 characters",
            "validation.employee.firstNameMaxLength",
            AlertMessageKeys.Args(("max", 100), ("field", "firstName"))));

        var decoded = ApiMessageKeys.DecodeArgs(
            context.Response.Headers[ApiMessageKeys.ArgsHeaderName].ToString());

        Assert.NotNull(decoded);
        Assert.Equal(100L, decoded!["max"]);
        Assert.Equal("firstName", decoded["field"]);
    }

    [Fact]
    public void A_whole_number_comes_back_a_whole_number()
    {
        // A quantity or a limit that came back as 12.0 would print the same digits, so this
        // is not a visible bug — but the value handed to a template would no longer be the
        // integer the rule computed, and the day a template does arithmetic on it, it is.
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Validation(
            "x", "validation.employee.firstNameMaxLength", AlertMessageKeys.Args(("max", 100))));

        var decoded = ApiMessageKeys.DecodeArgs(context.Response.Headers[ApiMessageKeys.ArgsHeaderName].ToString());

        Assert.IsType<long>(decoded!["max"]);
    }

    [Fact]
    public void An_argument_cannot_break_the_header_it_travels_in()
    {
        // Some arguments are interpolated sentences of their own — a failure that quotes an
        // exception message, say. JSON's quotes are legal in a header value, but a value
        // carrying a newline would end the header, and a stray CRLF in a response is a
        // request-smuggling primitive rather than a bug. Percent-encoding rules all of it out.
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Unexpected(
            "Failed to store file", "validation.file.storeFailed", AlertMessageKeys.Args(("detail", "a \"quoted\"\r\nvalue"))));

        var header = context.Response.Headers[ApiMessageKeys.ArgsHeaderName].ToString();

        Assert.DoesNotContain('\r', header);
        Assert.DoesNotContain('\n', header);
        Assert.DoesNotContain('"', header);
        Assert.Matches("^[A-Za-z0-9%._~!$&'()*+,;=:@-]+$", header);
        Assert.Equal("a \"quoted\"\r\nvalue", ApiMessageKeys.DecodeArgs(header)!["detail"]);
    }

    [Fact]
    public void An_enormous_argument_is_dropped_and_the_key_still_goes_out()
    {
        // Documented degradation: a pathological argument must not be able to push the
        // response over a proxy's header limit and have the whole thing rejected, which
        // would turn a working English error into a bare 502. Losing the arguments costs a
        // translated sentence; losing the response costs the reader the error itself.
        var context = new DefaultHttpContext();
        ApiMessageKeys.Attach(context.Response, Error.Unexpected(
            "Failed to store file", "validation.file.storeFailed",
            AlertMessageKeys.Args(("detail", new string('x', ApiMessageKeys.MaxArgsHeaderLength * 2)))));

        Assert.Equal("validation.file.storeFailed", context.Response.Headers[ApiMessageKeys.HeaderName].ToString());
        Assert.False(context.Response.Headers.ContainsKey(ApiMessageKeys.ArgsHeaderName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%zz")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void An_args_header_the_client_cannot_read_decodes_to_nothing(string? encoded)
    {
        // The client returns null for each of these and falls back to the English body. A
        // header it cannot parse is a missing argument, not a thrown error.
        Assert.Null(ApiMessageKeys.DecodeArgs(encoded));
    }

    /// <summary>
    /// Every header constant <see cref="ApiMessageKeys"/> declares, by name. The list is
    /// discovered rather than written out, so a third header added to that class is covered
    /// the day it exists.
    /// </summary>
    private static List<string> HeaderConstants() =>
        typeof(ApiMessageKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral
                && field.FieldType == typeof(string)
                && field.Name.EndsWith("HeaderName", StringComparison.Ordinal))
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The header constants the CORS policy in <c>Program.cs</c> does <em>not</em> expose.
    /// <para>
    /// The policy names the constants rather than the literal header names, so that is what
    /// this compares — a header added to <see cref="ApiMessageKeys"/> and left out of the
    /// policy is exactly the half-finished change that has to fail the build.
    /// </para>
    /// </summary>
    private static List<string> UnexposedHeaders(string programSource)
    {
        var exposed = Regex.Match(
            programSource, @"WithExposedHeaders\((?<names>[^)]*)\)", RegexOptions.Singleline);
        if (!exposed.Success)
        {
            return HeaderConstants();
        }

        var names = exposed.Groups["names"].Value;
        return HeaderConstants().Where(name => !names.Contains(name, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Every_header_the_api_attaches_is_exposed_to_the_browser()
    {
        // The frontend and the API are on different origins, so a custom response header is
        // invisible to the browser unless the server names it here. That failure is silent by
        // construction: the header arrives, the client cannot read it, and every message
        // degrades to English with nothing in any log to say why.
        var program = File.ReadAllText(
            Path.Combine(ServerSources.RepositoryRoot(), "src", "FMS.API", "Program.cs"));

        Assert.Empty(UnexposedHeaders(program));
    }

    [Fact]
    public void The_header_names_are_the_ones_the_client_reads()
    {
        // The client's reader matches on the lower-cased header name, so these two strings
        // are a contract with client/src/i18n/serverMessage.ts rather than a naming
        // preference. Renaming one here would leave the client reading a header nobody sends.
        Assert.Equal("X-Message-Key", ApiMessageKeys.HeaderName);
        Assert.Equal("X-Message-Args", ApiMessageKeys.ArgsHeaderName);
        Assert.Equal(new[] { "ArgsHeaderName", "HeaderName" }, HeaderConstants());
    }

    [Fact]
    public void The_cors_guard_would_notice_a_header_nobody_exposed()
    {
        // A guard that cannot fail reads as coverage. The shape it must catch is a header
        // added to ApiMessageKeys and left out of the policy — the exact half-finished change
        // this exists to catch, run through the same code the real assertion uses.
        const string halfFinished = @"
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(""https://example.test"")
    .AllowAnyHeader()
    .AllowCredentials()
    .WithExposedHeaders(ApiMessageKeys.HeaderName)));";

        Assert.Equal(new[] { "ArgsHeaderName" }, UnexposedHeaders(halfFinished));

        // And the finished version is clean, so the assertion above is not passing vacuously.
        const string finished = @"
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithExposedHeaders(ApiMessageKeys.HeaderName, ApiMessageKeys.ArgsHeaderName)));";
        Assert.Empty(UnexposedHeaders(finished));
    }
}
