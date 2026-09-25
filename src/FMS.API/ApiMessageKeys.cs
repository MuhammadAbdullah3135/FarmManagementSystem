using FMS.Application.Common;

namespace FMS.API;

/// <summary>
/// Carries a domain failure's i18n key to the client alongside the response it already
/// returns, without changing that response.
///
/// <para>
/// The message key travels as a header rather than in the body on purpose. Every error
/// response today is a plain string (or an RFC 7807 problem whose <c>detail</c> is that
/// string), and the project's testing discipline across 3.4/4.3/6.1 asserts on those exact
/// bytes. Wrapping them in an envelope to make room for a key would rewrite every one of
/// those assertions for no user-visible gain; a header is additive, invisible to a caller
/// that ignores it, and lets the client prefer <c>t(key)</c> when it has one.
/// </para>
///
/// <para>
/// The header must also be listed in <c>WithExposedHeaders</c> on the CORS policy,
/// otherwise a browser on a different origin (the GitHub Pages frontend talking to the
/// Heroku API) cannot read it.
/// </para>
/// </summary>
public static class ApiMessageKeys
{
    public const string HeaderName = "X-Message-Key";

    /// <summary>Adds the header when the failure has a key; a keyless failure is left alone.</summary>
    public static void Attach(HttpResponse response, Error error)
    {
        if (!string.IsNullOrEmpty(error.MessageKey))
        {
            response.Headers[HeaderName] = error.MessageKey;
        }
    }
}
