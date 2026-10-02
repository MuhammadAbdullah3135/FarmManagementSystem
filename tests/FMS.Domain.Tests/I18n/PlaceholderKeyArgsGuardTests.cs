using System.Reflection;
using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Stops a sentence that needs an argument from being sent without one.
/// <para>
/// 18 keys in the bundles carry <c>{{max}}</c>-style placeholders, and every call site that
/// emits one passes the arguments — this class asserts exactly that, so the defect cannot come
/// back at the source. It matters because the failure is invisible from the outside: the
/// response still arrives, the status code is right, the key is present and correct, and the
/// only symptom is a brace in a sentence three layers down. That is precisely what shipped to
/// production, because the transport dropped the arguments and every test that exercised the
/// key said the feature worked.
/// </para>
/// <para>
/// The key is read from the client's own bundle, so a placeholder that appears in a new
/// locale string is covered without this test being edited, and a key that stops needing an
/// argument simply stops matching. Both call shapes are checked: a factory
/// (<c>Error.Validation(message, key, args)</c>) and a <c>FieldError</c> (the rules build
/// those and a service forwards them, so the placeholder usually appears in the rules, not in
/// the service).
/// </para>
/// <para>
/// Source scan rather than reflection over the domain, because the argument is an optional
/// parameter on a type rather than a type of its own: nothing at runtime distinguishes
/// "passed nothing" from "passed a dictionary", so a runtime assertion could not fail.
/// </para>
/// </summary>
public class PlaceholderKeyArgsGuardTests
{
    /// <summary>
    /// Every key whose English sentence needs an argument, read from the shipped bundle.
    /// The client is the only place that knows which sentences interpolate.
    /// </summary>
    private static HashSet<string> PlaceholderKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, sentence) in ClientBundles.Bundle("en", "validation"))
        {
            if (Regex.IsMatch(sentence, @"\{\{\w+\}\}"))
            {
                keys.Add($"validation.{path}");
            }
        }

        return keys;
    }

    /// <summary>
    /// A key may be written as a literal or referenced through a constant, so both are
    /// matched. Reflection over the constants means a key added to
    /// <c>DomainMessageKeys</c> is covered the day it is declared.
    /// </summary>
    private static Dictionary<string, string> KeyConstants() =>
        typeof(FMS.Application.Common.DomainMessageKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (Name: field.Name, Value: (string)field.GetRawConstantValue()!))
            .Where(pair => pair.Value.Contains('.'))
            .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void No_site_sends_a_placeholder_key_without_its_arguments()
    {
        var placeholderKeys = PlaceholderKeys();
        var constants = KeyConstants();
        var offenders = new List<string>();

        // The argument that carries the arguments, by 0-based position, for each shape that
        // can carry a key. A factory is (message, key, args); a FieldError is
        // (field, message, key, args).
        var shapes = new (Regex Call, int ArgsIndex)[]
        {
            (new Regex(@"\.(?:NotFound|Validation|Unauthorized|Conflict|Unexpected|Unavailable|Superseded|Failure)\s*\(", RegexOptions.Compiled), 2),
            (new Regex(@"new\s+FieldError\s*\(", RegexOptions.Compiled), 3),
        };

        foreach (var file in ServerSources.SourceFiles())
        {
            var code = ServerSources.WithoutComments(File.ReadAllText(file));

            foreach (var (call, argsIndex) in shapes)
            {
                foreach (Match match in call.Matches(code))
                {
                    var open = match.Index + match.Length - 1;
                    var args = SplitArguments(code, open);
                    if (args is null)
                    {
                        continue;
                    }

                    // The key is the argument before the arguments.
                    if (argsIndex - 1 >= args.Count)
                    {
                        continue;
                    }

                    var key = ReadKeyArgument(args[argsIndex - 1]);
                    if (key is null)
                    {
                        continue;
                    }

                    var isPlaceholder = placeholderKeys.Contains(key)
                        || (constants.TryGetValue(key, out var constant) && placeholderKeys.Contains(constant));
                    if (!isPlaceholder)
                    {
                        continue;
                    }

                    if (args.Count <= argsIndex)
                    {
                        var line = code[..match.Index].Count(c => c == '\n') + 1;
                        offenders.Add(
                            $"{ServerSources.Relative(file)}:{line}  {key}  — the sentence needs an "
                            + "argument, so the client would render the placeholder itself");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These sites send a key whose sentence takes an argument, without passing it. The "
            + "client renders the key in preference to the English body, so the reader sees a "
            + "literal {{placeholder}} in an otherwise translated sentence — worse than the "
            + "English it replaced. Pass the arguments as the next argument:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_guard_itself_would_notice_a_site_that_forgets_its_arguments()
    {
        // Both shapes, and the constant form, exercised directly. A guard that cannot fail
        // reads as coverage, which is the mistake this suite has already made once.
        Assert.Contains("validation.employee.firstNameMaxLength", PlaceholderKeys());
        Assert.Contains("validation.inventoryItem.nameMaxLength", PlaceholderKeys());

        // Forgetting: the key is there, the argument is not. This is the shipped bug.
        const string forgetting =
            "Error.Validation(\"First name cannot exceed 100 characters\", \"validation.employee.firstNameMaxLength\")";
        var forgot = SplitArguments(forgetting, forgetting.IndexOf('('))!;
        Assert.Equal(2, forgot.Count);
        Assert.Equal("validation.employee.firstNameMaxLength", ReadKeyArgument(forgot[1]));

        // Remembering, as a literal dictionary — the shape EmployeeRules uses. The
        // interpolated message carries braces and a nested call, neither of which may be
        // mistaken for argument boundaries.
        const string remembering =
            "new FieldError(\"FirstName\", $\"First name cannot exceed {NameMaxLength} characters\", "
            + "\"validation.employee.firstNameMaxLength\", new Dictionary<string, object?> { [\"max\"] = NameMaxLength })";
        var remembered = SplitArguments(remembering, remembering.IndexOf('('))!;
        Assert.Equal(4, remembered.Count);
        Assert.Equal("validation.employee.firstNameMaxLength", ReadKeyArgument(remembered[2]));

        // Remembering, by forwarding another error's arguments. The key here is
        // `errors[0].MessageKey` — an indexed member access this guard deliberately does not
        // read, because whatever rule produced that error also produced the arguments being
        // forwarded alongside it, and that rule is scanned in its own right.
        const string forwarding =
            "Result<EmployeeDto>.Validation(errors[0].Message, errors[0].MessageKey, errors[0].MessageArgs)";
        var forwarded = SplitArguments(forwarding, forwarding.IndexOf('('))!;
        Assert.Equal(3, forwarded.Count);
        Assert.Null(ReadKeyArgument(forwarded[1]));
        Assert.Contains("MessageArgs", forwarded[2]);

        // The constant form resolves through reflection, so a key added to DomainMessageKeys
        // is covered the day it is declared. Every one of today's constants is a lookup key
        // with no placeholder, which is why none of them is caught above.
        var constants = KeyConstants();
        Assert.Equal("validation.lookup.animal", constants["AnimalNotFound"]);
        Assert.DoesNotContain(constants["AnimalNotFound"], PlaceholderKeys());

        // A sentence with a comma in it must not be split at the comma.
        const string comma = "Error.Validation(\"First, name, required\", \"validation.employee.firstNameRequired\")";
        Assert.Equal(2, SplitArguments(comma, comma.IndexOf('('))!.Count);

        // Nor may the comma in a type-argument list. This one was a real bug: the
        // arguments dictionary is written `new Dictionary<string, object?>` everywhere, so
        // every call that passed one parsed as having an extra argument after it.
        Assert.Equal(4, remembered.Count);

        // A comparison is not a type-argument list, and must not be counted as one.
        const string comparison = "Error.Validation(count < limit ? \"too few\" : \"fine\", \"validation.employee.firstNameRequired\")";
        Assert.Equal(2, SplitArguments(comparison, comparison.IndexOf('('))!.Count);

        // A key with no placeholder must not be caught by this guard: it needs no argument.
        Assert.DoesNotContain("validation.breeding.recordInUse", PlaceholderKeys());
    }

    /// <summary>
    /// The key argument, whether it was written as a literal or as a constant name, or null
    /// when it is neither (a variable, which this guard leaves alone — see below).
    /// </summary>
    private static string? ReadKeyArgument(string argument)
    {
        // Splitting on commas keeps the whitespace that separated the arguments, so the text
        // is trimmed before it is matched rather than each pattern absorbing leading space.
        argument = argument.Trim();

        var literal = Regex.Match(argument, "^@?\"(?<key>[^\"]+)\"$");
        if (literal.Success)
        {
            return literal.Groups["key"].Value;
        }

        var reference = Regex.Match(argument, @"^\s*(?:[A-Za-z_][\w]*\.)?(?<name>[A-Za-z_]\w*)\s*$");
        return reference.Success ? reference.Groups["name"].Value : null;
    }

    /// <summary>
    /// The call's top-level arguments as source text, or null when the parentheses do not
    /// balance.
    /// <para>
    /// Depth counting alone would be wrong, because an interpolated message carries braces
    /// and parentheses of its own (<c>$"...{Foo(x, y)}..."</c>) that must not be counted as
    /// structure. Strings are therefore skipped whole, honouring both the <c>\"</c> escape and
    /// the doubled quote of a verbatim string, so a comma inside a sentence is never mistaken
    /// for an argument boundary.
    /// </para>
    /// </summary>
    private static List<string>? SplitArguments(string code, int openParen)
    {
        var args = new List<string>();
        var depth = 0;
        var angle = 0;
        var start = openParen + 1;
        var index = openParen;
        char? quote = null;
        var verbatim = false;

        for (; index < code.Length; index++)
        {
            var c = code[index];

            if (quote is not null)
            {
                if (verbatim)
                {
                    if (c == '"' && index + 1 < code.Length && code[index + 1] == '"')
                    {
                        index++;
                    }
                    else if (c == '"')
                    {
                        quote = null;
                    }
                }
                else if (c == '\\')
                {
                    index++;
                }
                else if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c == '"' || (c == '@' && index + 1 < code.Length && code[index + 1] == '"'))
            {
                // A verbatim string is introduced by @, an ordinary one by ". Either way the
                // scanner is now inside a string and skips to its closing quote.
                var startsVerbatim = c == '@';
                if (startsVerbatim)
                {
                    index++;
                }

                quote = '"';
                verbatim = startsVerbatim;
                continue;
            }

            // A type-argument list, and only that: the bracket has to hug an identifier on
            // both sides, which is how C# is written everywhere in this repository
            // (`Dictionary<string, object?>`) and is not how a comparison is
            // (`a < b`). Without this, the comma in `Dictionary<string, object?>` reads as
            // an argument boundary and a four-argument call parses as five — which is
            // exactly the bug this scanner had before it was pinned by a test.
            if (c == '<' && index > 0 && index + 1 < code.Length
                && IsIdentifierChar(code[index - 1])
                && (IsIdentifierChar(code[index + 1]) || code[index + 1] == '"'))
            {
                angle++;
                continue;
            }

            if (c == '>' && angle > 0)
            {
                angle--;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                {
                    args.Add(code[start..index]);
                    return args;
                }
            }
            else if (c == ',' && depth == 1 && angle == 0)
            {
                args.Add(code[start..index]);
                start = index + 1;
            }
        }

        return null;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '>';
}
