namespace FMS.Application.Import;

/// <summary>
/// One column an importer understands, in the order the template and the mapping
/// table present them.
/// </summary>
/// <param name="Key">Stable key the client maps against; never shown to the user.</param>
/// <param name="Label">Human label, also the header written into the CSV template.</param>
/// <param name="Required">True when a row cannot be created without it.</param>
/// <param name="Hint">Short guidance shown beside the mapping row.</param>
public record ImportFieldDescriptor(string Key, string Label, bool Required, string Hint);

/// <summary>
/// The columns one entity's importer understands, and how a file's own headers map
/// onto them.
///
/// This is deliberately a fixed, entity-specific vocabulary rather than a discovered
/// schema: the column-mapping model, the reader and the row-result DTOs are all
/// entity-agnostic, but the fields an animal or an employee has are not. So each
/// importer supplies its own catalog and everything above the catalog is shared.
/// </summary>
public interface IImportFieldCatalog
{
    IReadOnlyList<ImportFieldDescriptor> All { get; }

    /// <summary>The field a file header maps to, or null when nothing matches.</summary>
    string? MatchHeader(string header);

    ImportFieldDescriptor? Describe(string key);
}

/// <summary>
/// The catalog implementation every importer builds from a field list and a table of
/// header aliases. Header matching is always done on <see cref="Normalize"/>, so a
/// header's spacing, case and punctuation cannot stop it from matching — which is the
/// difference between a farm's existing spreadsheet importing on the first try and
/// being told every column is unrecognised.
/// </summary>
public sealed class ImportFieldCatalog : IImportFieldCatalog
{
    private readonly Dictionary<string, string[]> _aliases;

    public ImportFieldCatalog(
        IEnumerable<ImportFieldDescriptor> fields,
        IReadOnlyDictionary<string, string[]> aliases)
    {
        All = fields.ToList();
        _aliases = new Dictionary<string, string[]>(aliases, StringComparer.Ordinal);
    }

    public IReadOnlyList<ImportFieldDescriptor> All { get; }

    public ImportFieldDescriptor? Describe(string key) =>
        All.FirstOrDefault(field => field.Key == key);

    /// <summary>
    /// The field a header maps to, or null. The label and the field key are always
    /// accepted, so a file exported with this app's own template round-trips, and the
    /// aliases cover the spellings farms actually use.
    /// </summary>
    public string? MatchHeader(string header)
    {
        var normalized = Normalize(header);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var field in All)
        {
            if (Normalize(field.Key) == normalized || Normalize(field.Label) == normalized)
            {
                return field.Key;
            }
        }

        foreach (var (key, aliases) in _aliases)
        {
            if (aliases.Any(alias => Normalize(alias) == normalized))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// Lower-cases and strips everything but letters and digits, so "Tag No." and
    /// "tag_number" normalize to the same thing.
    ///
    /// Digits are deliberately kept rather than stripped with the punctuation: a
    /// header that differs from another only by a number should stay distinguishable
    /// rather than collapse into a match.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
