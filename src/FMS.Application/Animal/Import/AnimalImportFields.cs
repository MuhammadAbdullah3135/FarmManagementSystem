using FMS.Application.Import;

namespace FMS.Application.Animal.Import;

/// <summary>
/// The animal importer's field vocabulary.
///
/// This is deliberately a fixed, entity-specific list rather than a discovered
/// schema: the column-mapping model, the reader and the row-result DTOs are all
/// entity-agnostic, but the fields an animal has are not. Employees and inventory
/// supply their own lists in the same shape.
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

    public static readonly IReadOnlyList<ImportFieldDescriptor> All = new[]
    {
        new ImportFieldDescriptor(TagNumber, "Tag number", true,
            "Unique within the farm, e.g. TAG-0001"),
        new ImportFieldDescriptor(Name, "Name", false,
            "Optional, e.g. Bella"),
        new ImportFieldDescriptor(AnimalType, "Animal type", true,
            "Must match an animal type in this farm, e.g. Cattle"),
        new ImportFieldDescriptor(Breed, "Breed", false,
            "Must belong to the animal type"),
        new ImportFieldDescriptor(Sex, "Sex", true,
            "Must match a sex option in this farm, e.g. Female"),
        new ImportFieldDescriptor(AgeCategory, "Age category", false,
            "Must match an age category in this farm"),
        new ImportFieldDescriptor(Status, "Status", true,
            "Must match an animal status in this farm, e.g. Active"),
        new ImportFieldDescriptor(Location, "Location", false,
            "Must match a location in this farm"),
        new ImportFieldDescriptor(DateOfBirth, "Date of birth", false,
            "yyyy-MM-dd, or a real Excel date cell"),
        new ImportFieldDescriptor(AcquisitionDate, "Acquisition date", false,
            "yyyy-MM-dd, or a real Excel date cell"),
        new ImportFieldDescriptor(SireTag, "Sire tag", false,
            "Tag of the father: an existing animal, or another row in this file"),
        new ImportFieldDescriptor(DamTag, "Dam tag", false,
            "Tag of the mother: an existing animal, or another row in this file"),
        new ImportFieldDescriptor(Notes, "Notes", false,
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

    /// <summary>
    /// The shared implementation of header matching, so every importer's detection
    /// behaves identically instead of each one growing its own spelling rules.
    ///
    /// Declared last on purpose: static fields initialize in textual order, and this
    /// one is built from <see cref="All"/> and <see cref="Aliases"/> above.
    /// </summary>
    private static readonly ImportFieldCatalog Catalog = new(All, Aliases);

    /// <summary>This vocabulary as the shared pipeline consumes it.</summary>
    public static IImportFieldCatalog FieldCatalog => Catalog;

    public static ImportFieldDescriptor? Describe(string key) => Catalog.Describe(key);

    /// <summary>
    /// Lower-cases and strips everything but letters and digits, so a header's
    /// spacing, case and punctuation cannot stop it from matching.
    /// </summary>
    public static string Normalize(string? value) => ImportFieldCatalog.Normalize(value);

    /// <summary>
    /// The field a header maps to, or null. The label and the field key are always
    /// accepted, so a file exported with this app's own template round-trips, and
    /// the aliases cover the spellings farms actually use.
    /// </summary>
    public static string? MatchHeader(string header) => Catalog.MatchHeader(header);
}
