using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using FMS.Application.Import;
using FMS.Domain.Common;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm.Export;

/// <summary>
/// The one fact about the domain model the export's reflection depends on: where its
/// entities live. Typed here once so the scalar/reference split cannot be made by two
/// different rules in two different places.
/// </summary>
internal static class FarmExportModel
{
    public const string DomainEntityNamespace = "FMS.Domain.Entities";

    /// <summary>True for a reference navigation to a domain entity, never for a collection.</summary>
    public static bool IsEntityReference(Type type) =>
        type != typeof(string)
        && !type.IsValueType
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
        && type.Namespace == DomainEntityNamespace;
}

/// <summary>
/// One entity's file inside the archive: how to read that farm's rows for it, and what
/// its columns are.
///
/// <para>
/// The farm id is a required argument of <see cref="ReadRowsAsync"/> rather than
/// something a table closes over, so the farm scoping is visible at the one place rows
/// are produced. That is deliberate: the failure mode this guards against — an archive
/// that quietly contains another farm's records — is invisible in a diff and would
/// otherwise depend on each of fifty-odd tables remembering its own filter.
/// </para>
///
/// <para>
/// Rows are streamed, never listed: a large farm's weight records are tens of thousands
/// of rows, and each one is written into the deflate stream as it arrives.
/// </para>
/// </summary>
public interface IFarmExportTable
{
    /// <summary>The entity's CLR type, so coverage of the model can be asserted.</summary>
    Type EntityType { get; }

    /// <summary>The entity name, e.g. <c>Animals</c>.</summary>
    string Entity { get; }

    /// <summary>The entry name inside the ZIP, e.g. <c>animals.csv</c>.</summary>
    string FileName { get; }

    /// <summary>
    /// True when this entity has an import endpoint that accepts this very file without
    /// a column mapping. Set from the entity's own field catalog, never asserted by hand.
    /// </summary>
    bool Reimportable { get; }

    IReadOnlyList<string> Columns { get; }

    IAsyncEnumerable<string[]> ReadRowsAsync(
        FmsDbContext db,
        Guid farmId,
        CancellationToken cancellationToken);
}

/// <summary>
/// A typed export table. The three factories below are the only ways to build one:
/// <see cref="ForImport"/> for the entities that have an importer,
/// <see cref="ReflectedForFarm"/> for a farm-scoped entity, and <see cref="Reflected"/>
/// for a child table that is scoped through its parent.
/// </summary>
public sealed class FarmExportTable<T> : IFarmExportTable where T : BaseEntity
{
    private readonly Func<FmsDbContext, Guid, IQueryable<T>> _query;
    private readonly Func<T, string[]> _project;

    private FarmExportTable(
        string entity,
        string fileName,
        bool reimportable,
        IReadOnlyList<string> columns,
        Func<FmsDbContext, Guid, IQueryable<T>> query,
        Func<T, string[]> project)
    {
        Entity = entity;
        FileName = fileName;
        Reimportable = reimportable;
        Columns = columns;
        _query = query;
        _project = project;
    }

    public Type EntityType => typeof(T);

    public string Entity { get; }

    public string FileName { get; }

    public bool Reimportable { get; }

    public IReadOnlyList<string> Columns { get; }

    public async IAsyncEnumerable<string[]> ReadRowsAsync(
        FmsDbContext db,
        Guid farmId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in _query(db, farmId).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return _project(row);
        }
    }

    /// <summary>
    /// Builds a table whose columns are exactly an importer's field labels, in the
    /// importer's own order, so the file re-imports with no column mapping.
    ///
    /// The values map is required to cover every field in the catalog: a field the
    /// catalog gains but this table does not project fails loudly here rather than
    /// becoming a silently missing column in an archive that claims to be complete.
    /// </summary>
    public static FarmExportTable<T> ForImport(
        string entity,
        string fileName,
        IImportFieldCatalog catalog,
        Func<FmsDbContext, Guid, IQueryable<T>> query,
        IReadOnlyDictionary<string, Func<T, string>> values)
    {
        var fields = catalog.All;

        var missing = fields
            .Where(field => !values.ContainsKey(field.Key))
            .Select(field => field.Key)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{entity}' export table does not project the importer field(s): {string.Join(", ", missing)}. "
                + "Every field in the catalog must have a column, or the exported file cannot be re-imported.");
        }

        var columns = fields.Select(field => field.Label).ToList();

        return new FarmExportTable<T>(
            entity,
            fileName,
            reimportable: true,
            columns,
            query,
            row => fields.Select(field => values[field.Key](row) ?? string.Empty).ToArray());
    }

    /// <summary>
    /// A table for a farm-scoped entity whose rows carry <c>FarmId</c>.
    ///
    /// The type check is not defensive tidiness: without a <c>FarmId</c> property this
    /// query would be unbuildable, and a table built some other way would export every
    /// farm's rows into one farm's archive. Failing here makes that impossible.
    ///
    /// <para>
    /// Both <c>Guid</c> and <c>Guid?</c> are accepted. Audit rows are nullable because an
    /// account-level action belongs to no farm — and those rows are exactly the ones a
    /// farm-scoped filter must leave out, since <c>WHERE FarmId = @farm</c> never matches
    /// null either way the column is declared.
    /// </para>
    /// </summary>
    public static FarmExportTable<T> ReflectedForFarm(string entity, string fileName)
    {
        // The property name is a string because it is resolved on the entity, not on
        // this class: the check below is what makes the string safe to use.
        const string FarmIdProperty = "FarmId";

        var propertyType = typeof(T).GetProperty(FarmIdProperty)?.PropertyType;

        if (propertyType != typeof(Guid) && propertyType != typeof(Guid?))
        {
            throw new InvalidOperationException(
                $"'{entity}' has no Guid FarmId property, so a farm-scoped export table cannot be built for it. "
                + "Scope it through its parent with Reflected(...) instead.");
        }

        // The branch has to be decided on the declared type before the expression is built:
        // EF.Property<Guid> over a nullable column fails to translate, and the reverse
        // comparison never matches.
        return Reflected(entity, fileName, (db, farmId) => propertyType == typeof(Guid)
            ? db.Set<T>().AsNoTracking().Where(row => EF.Property<Guid>(row, FarmIdProperty) == farmId)
            : db.Set<T>().AsNoTracking().Where(row => EF.Property<Guid?>(row, FarmIdProperty) == farmId));
    }

    /// <summary>
    /// A table for an entity with no importer: every scalar property becomes a column,
    /// and every reference navigation adds a <c>{Navigation}Name</c> column resolved
    /// from the related record's own display value.
    ///
    /// <para>
    /// Ids are kept alongside the names deliberately. A name is what makes the file
    /// readable and matchable by a person; the id is what makes it unambiguous when two
    /// records share a name. Neither alone is a faithful archive.
    /// </para>
    ///
    /// <para>
    /// <paramref name="scope"/> is where the farm filtering lives, and it is required
    /// rather than optional for that reason — see <see cref="ReflectedForFarm"/> for the
    /// common case and the child tables (breeds, diet-plan items) for the other.
    /// </para>
    /// </summary>
    public static FarmExportTable<T> Reflected(
        string entity,
        string fileName,
        Func<FmsDbContext, Guid, IQueryable<T>> scope)
    {
        var (columns, project) = ReflectedProjection<T>.Build();
        var includes = ReferenceNavigations<T>.Paths();

        return new FarmExportTable<T>(
            entity,
            fileName,
            reimportable: false,
            columns,
            (db, farmId) =>
            {
                var query = scope(db, farmId);

                foreach (var path in includes)
                {
                    query = query.Include(path);
                }

                // A stable order, so two exports of the same farm produce the same file.
                // Without it, a download that differs only in row order looks like a
                // change when the two are compared.
                return query.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id);
            },
            project);
    }
}

/// <summary>The reflection-driven projection behind <see cref="FarmExportTable{T}.Reflected"/>.</summary>
internal static class ReflectedProjection<T>
{
    public static (IReadOnlyList<string> Columns, Func<T, string[]> Project) Build()
    {
        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToList();

        var scalars = properties.Where(property => IsScalar(property.PropertyType)).ToList();
        var references = properties
            .Where(property => property.Name != "Farm")
            .Where(property => FarmExportModel.IsEntityReference(property.PropertyType))
            .ToList();

        var columns = scalars.Select(property => property.Name)
            .Concat(references.Select(property => $"{property.Name}Name"))
            .ToList();

        return (columns, row =>
        {
            var cells = new List<string>(columns.Count);

            foreach (var property in scalars)
            {
                cells.Add(FarmExportValue.Format(property.GetValue(row)));
            }

            foreach (var property in references)
            {
                cells.Add(FarmExportValue.Display(property.GetValue(row)));
            }

            return cells.ToArray();
        });
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsEnum
            || underlying == typeof(string)
            || underlying.IsPrimitive
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }
}

/// <summary>
/// The reference navigations of an entity, as include paths.
///
/// <c>Farm</c> is excluded: every farm row points at the farm whose archive this is, so
/// including it would join the same row into fifty-odd files for no information.
/// </summary>
internal static class ReferenceNavigations<T>
{
    public static IReadOnlyList<string> Paths() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => property.Name != "Farm")
            .Where(property => FarmExportModel.IsEntityReference(property.PropertyType))
            .Select(property => property.Name)
            .ToList();
}

/// <summary>
/// How a value is written into a CSV cell. Deterministic and culture-invariant: an
/// archive read on a machine with a comma decimal separator must be the same file.
/// </summary>
internal static class FarmExportValue
{
    /// <summary>Dates are written as plain UTC dates — the shape the importer reads back.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Timestamps keep their time of day, at second precision, marked UTC.</summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";    /// <summary>
    /// Property names that stand in for a related record's identity, most specific first.
    /// <c>TagNumber</c> is here because an animal's name is optional and its tag is not,
    /// so a reference to an animal is useless without falling back to it; <c>Value</c> is
    /// here because a lookup such as SexOption carries its label there rather than in a
    /// <c>Name</c> property.
    /// </summary>
    private static readonly string[] DisplayNames =
        ["Name", "Title", "TagNumber", "Label", "Code", "Email", "Value"]; 

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime date => FormatTimestamp(date),
        DateTimeOffset offset => offset.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        Guid id => id.ToString(),
        Enum named => named.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>A date with no time of day, e.g. a birth date or a hire date.</summary>
    public static string FormatDate(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value).ToString(DateFormat, CultureInfo.InvariantCulture) : string.Empty;

    public static string FormatTimestamp(DateTime value) =>
        AsUtc(value).ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// A value read back from a DateTime column is UTC by the model-wide converter, but a
    /// value constructed in memory may carry no kind at all. Treating an unspecified kind
    /// as UTC is the convention the whole schema relies on; guessing local time here would
    /// shift every timestamp in the archive by the server's offset.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// The display value of a related record: its name, or whatever identity it has, so
    /// <c>BreedName</c> reads "Holstein" and <c>AnimalName</c> reads "Bella" — or the tag
    /// when the animal has no name.
    /// </summary>
    public static string Display(object? related)
    {
        if (related is null)
        {
            return string.Empty;
        }

        var type = related.GetType();

        foreach (var name in DisplayNames)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType != typeof(string))
            {
                continue;
            }

            if (property.GetValue(related) is string text && text.Length > 0)
            {
                return text;
            }
        }

        // A person has no Name property: their identity is the two halves of it.
        var first = type.GetProperty("FirstName")?.GetValue(related) as string;
        var last = type.GetProperty("LastName")?.GetValue(related) as string;
        var full = string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return full.Length > 0 ? full : related.ToString() ?? string.Empty;
    }
}
