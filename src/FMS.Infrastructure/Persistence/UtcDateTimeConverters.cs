using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FMS.Infrastructure.Persistence;

/// <summary>
/// Stores every <see cref="DateTime"/> as a UTC instant.
/// </summary>
/// <remarks>
/// The schema stores all dates as <c>timestamp with time zone</c> and Npgsql refuses
/// <see cref="DateTimeKind.Unspecified"/> values against it, so a single <c>new DateTime(year, 1, 1)</c>
/// or a date parsed without a zone used to fail at the database instead of in a test. The API
/// normalises what it receives (see <c>FMS.API.Binding.UtcDateTime</c>); this converter is the
/// matching guarantee for anything the server constructs itself — background jobs, seeders,
/// imports and future code — so no write can reach PostgreSQL with an ambiguous kind.
/// </remarks>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Utc
                ? value
                : value.Kind == DateTimeKind.Local
                    ? value.ToUniversalTime()
                    : DateTime.SpecifyKind(value, DateTimeKind.Utc),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            value => !value.HasValue
                ? value
                : value.Value.Kind == DateTimeKind.Utc
                    ? value
                    : value.Value.Kind == DateTimeKind.Local
                        ? value.Value.ToUniversalTime()
                        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value)
    {
    }
}
