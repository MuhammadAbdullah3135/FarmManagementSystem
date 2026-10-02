using System.Reflection;
using System.Text.Json;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// The client's locale bundles, read from the working tree, for the tests that assert the
/// server's message keys are keys the client can actually render.
/// <para>
/// The two halves of the design meet here and nowhere else: the server states what happened
/// and the client decides how to say it, so a key that exists on only one side does not
/// throw. It degrades — the client falls back to the English text the server also sent —
/// which is exactly the silent-English failure the whole key mechanism exists to remove, and
/// exactly why it is asserted rather than assumed.
/// </para>
/// <para>
/// One implementation, shared: 7.3 added Arabic, and a second copy of the directory walk
/// would be a second list of locales to forget to update. The directories are discovered,
/// never hard-coded.
/// </para>
/// </summary>
internal static class ClientBundles
{
    /// <summary>The client's resource directory, found by walking up from the test binary.</summary>
    public static string LocalesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "client", "src", "i18n", "locales");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "client/src/i18n/locales was not found above the test binary. This test compares the " +
            "server's message keys with the client's bundles, so it needs the whole repository.");
    }

    /// <summary>
    /// One namespace's keys for a locale, flattened so that a nested group arrives under its
    /// dotted path (<c>lookup.animal</c>) — the form a server key's path takes once its first
    /// dot has been split off as the namespace. Mirrors the client's own <c>flatten</c>.
    /// </summary>
    public static Dictionary<string, string> Bundle(string locale, string namespaceName)
    {
        var file = Path.Combine(LocalesDirectory(), locale, $"{namespaceName}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(file));

        var flat = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(document.RootElement, prefix: null, flat);
        return flat;

        static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> into)
        {
            foreach (var property in element.EnumerateObject())
            {
                var path = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    Flatten(property.Value, path, into);
                }
                else
                {
                    into[path] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// Every <c>const string</c> a message-key class declares, in a stable order.
    /// <para>
    /// Reflection rather than a hand-written list, so a key added tomorrow is covered the
    /// moment its constant exists — which is the only way a check like this stays true.
    /// The <c>Contains('.')</c> filter skips any non-key constant the class may grow.
    /// </para>
    /// </summary>
    public static List<string> DeclaredKeys(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.Contains('.'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every locale the client ships apart from English, in a stable order.</summary>
    public static List<string> TranslatedLocales()
    {
        var locales = Directory.GetDirectories(LocalesDirectory())
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && name != "en")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (locales.Count == 0)
        {
            throw new DirectoryNotFoundException(
                "No translated locale directories were found beside en/ in client/src/i18n/locales.");
        }

        return locales!;
    }

    /// <summary>
    /// Splits a server key into the namespace the client loads and the path inside it. A
    /// server key is always <c>namespace.path.to.key</c>, because the server has no business
    /// knowing i18next's <c>:</c> namespace separator.
    /// </summary>
    public static (string NamespaceName, string Path) SplitKey(string key)
    {
        var dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
        {
            throw new ArgumentException(
                $"'{key}' is not a namespace.path key, so the client would not resolve it.",
                nameof(key));
        }

        return (key[..dot], key[(dot + 1)..]);
    }
}
