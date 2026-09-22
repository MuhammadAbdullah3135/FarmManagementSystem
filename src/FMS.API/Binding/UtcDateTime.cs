using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FMS.API.Binding;

/// <summary>
/// One rule for every date/time that enters the API: a value with no time zone is UTC.
/// </summary>
/// <remarks>
/// Every date column in the database is <c>timestamp with time zone</c>, and Npgsql refuses to
/// write or compare a <see cref="DateTimeKind.Unspecified"/> value against one. Query-string
/// binding and <c>new DateTime(year, 1, 1)</c> both produce Unspecified, so a request as ordinary
/// as <c>?date=2026-09-22</c> used to blow up on PostgreSQL while passing on the EF InMemory
/// provider the test suite uses. Normalising here means the rest of the code can keep treating
/// dates as plain values.
/// </remarks>
public static class UtcDateTime
{
    /// <summary>
    /// Interprets <paramref name="value"/> as a UTC instant: unspecified values are taken to
    /// already be UTC (not shifted by the server's offset), offset values are converted.
    /// </summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>Parses a bound/JSON date string, or returns <c>null</c> when it is unusable.</summary>
    public static DateTime? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // RoundtripKind keeps an explicit designator ("Z", "+05:00") and leaves a bare
        // "2026-09-22" unspecified, which Normalize then reads as UTC rather than as
        // whatever time zone the server happens to run in.
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? Normalize(parsed)
            : null;
    }
}

/// <summary>Binds <c>[FromQuery] DateTime</c> / <c>DateTime?</c> through <see cref="UtcDateTime"/>.</summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            // Leave the model unset: a required DateTime still fails its own binding, and a
            // nullable one stays null exactly as it did before this binder existed.
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue;
        var isNullable = Nullable.GetUnderlyingType(bindingContext.ModelType) is not null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (isNullable)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "A date value is required.");
            }

            return Task.CompletedTask;
        }

        var parsed = UtcDateTime.TryParse(raw);
        if (parsed is null)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                $"The value '{raw}' is not a valid date and time.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(parsed);
        return Task.CompletedTask;
    }
}

/// <summary>Routes DateTime and DateTime? values to the UTC binder.</summary>
public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.ModelType;
        var isDateTime = type == typeof(DateTime) || type == typeof(DateTime?);
        if (!isDateTime)
        {
            return null;
        }

        // Only plain values: a body-bound DateTime belongs to the JSON formatter, which has its
        // own converter below, and a complex binder must keep handling its own properties.
        return context.Metadata.IsComplexType ? null : new UtcDateTimeModelBinder();
    }
}

/// <summary>Applies the same "naive means UTC" rule to dates inside request bodies.</summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            var parsed = UtcDateTime.TryParse(raw);
            if (parsed is null)
            {
                throw new JsonException($"The value '{raw}' is not a valid date and time.");
            }

            return parsed.Value;
        }

        return UtcDateTime.Normalize(reader.GetDateTime());
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(UtcDateTime.Normalize(value));
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeJsonConverter"/>.</summary>
public sealed class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var parsed = UtcDateTime.TryParse(raw);
            if (parsed is null)
            {
                throw new JsonException($"The value '{raw}' is not a valid date and time.");
            }

            return parsed.Value;
        }

        return UtcDateTime.Normalize(reader.GetDateTime());
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTime.Normalize(value.Value));
    }
}
