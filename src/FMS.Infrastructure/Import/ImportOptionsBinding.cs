using FMS.Application.Import;
using Microsoft.Extensions.Configuration;

namespace FMS.Infrastructure.Import;

/// <summary>
/// Keeps the pre-4.3 <c>AnimalImport</c> limits working now that one <c>Import</c>
/// section serves every entity.
///
/// A rename of a configuration key fails silently by nature: the app starts, the
/// section binds to nothing, and the limits quietly become the defaults. For an
/// installation that had raised <c>MaxRows</c> to accept a large herd register, that
/// is a regression with no error attached to it — so the old keys are still read,
/// and only for values the new section does not define.
///
/// <para>
/// Precedence is deliberate: <c>Import</c> wins whenever it sets a key, so the legacy
/// spelling can never override the documented one.
/// </para>
/// </summary>
public static class ImportOptionsBinding
{
    private static readonly string[] Keys =
    {
        nameof(ImportOptions.MaxRows),
        nameof(ImportOptions.MaxFileBytes),
        nameof(ImportOptions.MaxReportedRows),
        nameof(ImportOptions.SampleValidRows)
    };

    /// <summary>Fills each limit from the legacy section when the new section leaves it unset.</summary>
    public static void ApplyLegacyFallback(ImportOptions options, IConfiguration configuration)
    {
        foreach (var key in Keys)
        {
            if (IsSet(configuration, ImportOptions.SectionName, key))
            {
                continue;
            }

            var legacy = configuration[$"{ImportOptions.LegacySectionName}:{key}"];
            if (string.IsNullOrWhiteSpace(legacy))
            {
                continue;
            }

            // A value that will not parse is ignored rather than fatal: the new
            // section's default is a sane limit, and refusing to boot over a typo in a
            // deprecated key would be a worse outcome than using the default.
            var raw = legacy.Trim();

            switch (key)
            {
                case nameof(ImportOptions.MaxRows) when int.TryParse(raw, out var maxRows) && maxRows > 0:
                    options.MaxRows = maxRows;
                    break;
                case nameof(ImportOptions.MaxFileBytes) when long.TryParse(raw, out var maxFileBytes) && maxFileBytes > 0:
                    options.MaxFileBytes = maxFileBytes;
                    break;
                case nameof(ImportOptions.MaxReportedRows) when int.TryParse(raw, out var maxReportedRows) && maxReportedRows > 0:
                    options.MaxReportedRows = maxReportedRows;
                    break;
                case nameof(ImportOptions.SampleValidRows) when int.TryParse(raw, out var sampleValidRows) && sampleValidRows > 0:
                    options.SampleValidRows = sampleValidRows;
                    break;
            }
        }
    }

    private static bool IsSet(IConfiguration configuration, string section, string key) =>
        !string.IsNullOrWhiteSpace(configuration[$"{section}:{key}"]);
}
