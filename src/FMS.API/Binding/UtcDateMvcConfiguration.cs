using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.API.Binding;

public static class UtcDateMvcConfiguration
{
    /// <summary>
    /// Applies the "a date with no time zone means UTC" rule to the whole MVC pipeline: query
    /// strings, route values and JSON request/response bodies.
    /// </summary>
    /// <remarks>
    /// Deliberately one shared call rather than two identical registrations. The E2E test host
    /// builds its own <c>AddControllers()</c> pipeline instead of using Program.cs, so anything
    /// wired only in Program.cs is exercised by no test at all — exactly the kind of drift that
    /// let a date-only query value reach PostgreSQL as Unspecified and answer 400 in production
    /// while every test was green.
    /// </remarks>
    public static IMvcBuilder AddUtcDateHandling(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options =>
            options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider()));

        builder.AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
            options.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
        });

        return builder;
    }
}
