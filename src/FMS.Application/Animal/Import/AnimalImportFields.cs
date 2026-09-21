namespace FMS.Application.Animal.Import;

/// <summary>
/// One column the animal importer understands, in the order the template and the
/// mapping table present them.
/// </summary>
/// <param name="Key">Stable key the client maps against; never shown to the user.</param>
/// <param name="Label">Human label, also the header written into the CSV template.</param>
/// <param name="Required">True when a row cannot be created without it.</param>
/// <param name="Hint">Short guidance shown beside the mapping row.</param>
public record AnimalImportFieldDescriptor(string Key, string Label, bool Required, string Hint);

/// <summary>
/// The import's field vocabulary.
///
/// This is deliberately a fixed, entity-specific list rather than a discovered
/// schema: the column-mapping model, the reader and the row-result DTOs are all
/// entity-agnostic, but the fields an animal has are not. A future employee or
/// inventory import supplies its own field list and its own resolver instead of
/// sharing this one.
/// </summary>
public static class AnimalImportFields
{
    public const string TagNumber = "tagNumber";
    public const string Name = "name";
    public const string AnimalType = "animalType";
    public const string Breed = "breed";
    public const string Sex = "sex";
    public const string AgeCategory = "ageCategory";
    public const string Status = "status";
    public const string Location = "location";
    public const string DateOfBirth = "dateOfBirth";
    public const string AcquisitionDate = "acquisitionDate";
    public const string SireTag = "sireTag";
    public const string DamTag = "damTag";
    public const string Notes = "notes";

    /// <summary>Fields whose values are resolved against the farm's configuration.</summary>
    public static readonly IReadOnlyList<string> LookupFields = new[]
    {
        AnimalType, Breed, Sex, AgeCategory, Status, Location
    };

    /// <summary>Fields carrying a date, parsed with the ambiguous-format guard.</summary>
    public static readonly IReadOnlyList<string> DateFields = new[] { DateOfBirth, AcquisitionDate };

    public static readonly IReadOnlyList<AnimalImportFieldDescriptor> All = new[]
    {
        new AnimalImportFieldDescriptor(TagNumber, "Tag number", true,
            "Unique within the farm, e.g. TAG-0001"),
        new AnimalImportFieldDescriptor(Name, "Name", false,
            "Optional, e.g. Bella"),
        new AnimalImportFieldDescriptor(AnimalType, "Animal type", true,
            "Must match an animal type in this farm, e.g. Cattle"),
        new AnimalImportFieldDescriptor(Breed, "Breed", false,
            "Must belong to the animal type"),
        new AnimalImportFieldDescriptor(Sex, "Sex", true,
            "Must match a sex option in this farm, e.g. Female"),
        new AnimalImportFieldDescriptor(AgeCategory, "Age category", false,
            "Must match an age category in this farm"),
        new AnimalImportFieldDescriptor(Status, "Status", true,
            "Must match an animal status in this farm, e.g. Active"),
        new AnimalImportFieldDescriptor(Location, "Location", false,
            "Must match a location in this farm"),
        new AnimalImportFieldDescriptor(DateOfBirth, "Date of birth", false,
            "yyyy-MM-dd, or a real Excel date cell"),
        new AnimalImportFieldDescriptor(AcquisitionDate, "Acquisition date", false,
            "yyyy-MM-dd, or a real Excel date cell"),
        new AnimalImportFieldDescriptor(SireTag, "Sire tag", false,
            "Tag of the father: an existing animal, or another row in this file"),
        new AnimalImportFieldDescriptor(DamTag, "Dam tag", false,
            "Tag of the mother: an existing animal, or another row in this file"),
        new AnimalImportFieldDescriptor(Notes, "Notes", false,
            "Free text"),
    };

    /// <summary>
    /// Header spellings that auto-map to a field when the caller supplies no
    /// mapping. Matching is done on <see cref="Normalize"/>, so spacing, case and
    /// punctuation are irrelevant ("Tag No." and "tag_number" both match).
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [TagNumber] = new[] { "tag", "tag number", "tag no", "tag id", "tagnumber", "tagno", "animal tag", "ear tag" },
        [Name] = new[] { "name", "animal name", "nickname" },
        [AnimalType] = new[] { "animal type", "type", "species", "animaltype" },
        [Breed] = new[] { "breed", "breed name" },
        [Sex] = new[] { "sex", "gender" },
        [AgeCategory] = new[] { "age category", "age group", "age", "agecategory" },
        [Status] = new[] { "status", "animal status", "state", "condition" },
        [Location] = new[] { "location", "pen", "shed", "barn", "paddock" },
        [DateOfBirth] = new[] { "date of birth", "dob", "birth date", "birthdate", "born", "dateofbirth" },
        [AcquisitionDate] = new[] { "acquisition date", "acquired", "purchase date", "date acquired", "acquisitiondate" },
        [SireTag] = new[] { "sire", "sire tag", "father", "father tag", "siretag" },
        [DamTag] = new[] { "dam", "dam tag", "mother", "mother tag", "damtag" },
        [Notes] = new[] { "notes", "note", "comments", "remarks" },
    };

    public static AnimalImportFieldDescriptor? Describe(string key) =>
        All.FirstOrDefault(f => f.Key == key);

    /// <summary>
    /// Lower-cases and strips everything but letters and digits, so a header's
    /// spacing, case and punctuation cannot stop it from matching.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return new string(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    /// <summary>
    /// The field a header maps to, or null. The label and the field key are always
    /// accepted, so a file exported with this app's own template round-trips, and
    /// the aliases cover the spellings farms actually use.
    /// </summary>
    public static string? MatchHeader(string header)
    {
        var normalized = Normalize(header);
        if (normalized.Length == 0) return null;

        foreach (var field in All)
        {
            if (Normalize(field.Key) == normalized || Normalize(field.Label) == normalized)
                return field.Key;
        }

        foreach (var (key, aliases) in Aliases)
        {
            if (aliases.Any(alias => Normalize(alias) == normalized))
                return key;
        }

        return null;
    }
}
