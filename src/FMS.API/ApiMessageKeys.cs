using System.Text.Json;
using FMS.Application.Common;
using FMS.Application.Notifications;

namespace FMS.API;

/// <summary>
/// Carries a domain failure's i18n key — and the arguments its sentence needs — to the client
/// alongside the response it already returns, without changing that response.
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
///
/// <para>
/// <b>Arguments travel in a second header, not in the first.</b> A key like
/// <c>validation.employee.firstNameMaxLength</c> renders
/// "First name cannot exceed {{max}} characters", so the sentence is useless without its
/// arguments: the service computes them, puts them on the <see cref="Error"/>, and this
/// class used to drop them, so a Spanish reader was shown a literal <c>{{max}}</c> in a
/// Spanish sentence. Arguments have always travelled by body where one exists (the import
/// preview's per-row problems); this is the header path's version of the same idea.
/// </para>
/// </summary>
public static class ApiMessageKeys
{
    public const string HeaderName = "X-Message-Key";
    public const string ArgsHeaderName = "X-Message-Args";

    /// <summary>
    /// Longest arguments header this class will emit.
    ///
    /// <para>
    /// A pathological argument — a long exception message interpolated into a sentence, say
    /// — must not be able to push a response over a proxy's header limit and have the whole
    /// thing rejected, which would turn a working English error into a bare 502. Past this
    /// cap the arguments are dropped and the key is still sent, so the client falls back to
    /// the English body it has always had. Degrading to correct English beats degrading to
    /// nothing.
    /// </para>
    /// </summary>
    public const int MaxArgsHeaderLength = 4096;

    /// <summary>
    /// Adds the headers when the failure has a key; a keyless or absent failure is left alone.
    /// <para>
    /// The parameter is nullable so a call site can pass <c>result.Error</c> straight from a
    /// <c>Result</c> without a null check first. That is not a convenience: an inline mapping
    /// such as <c>return result.Error?.Code switch { ... }</c> has nowhere to put one, and
    /// nine of them silently dropped their key because the signature demanded a non-null error
    /// at a point where proving it was null would have meant restructuring the method.
    /// </para>
    /// </summary>
    public static void Attach(HttpResponse response, Error? error)
    {
        if (string.IsNullOrEmpty(error?.MessageKey))
        {
            return;
        }

        response.Headers[HeaderName] = error!.MessageKey;
        AttachArgs(response, error.MessageArgs);
    }

    /// <summary>
    /// Encodes the arguments as percent-encoded JSON and adds them as a header.
    ///
    /// <para>
    /// Percent-encoding rather than base64 because the client undoes it with a single
    /// <c>decodeURIComponent</c>, and because it cannot emit a character a proxy or header
    /// parser would object to: JSON's quotes and braces are legal in a header value but not
    /// worth the argument, and an interpolated value could contain anything at all.
    /// </para>
    /// </summary>
    public static void AttachArgs(HttpResponse response, IReadOnlyDictionary<string, object?>? args)
    {
        var json = MessageArgsJson.Serialize(args);
        if (json is null)
        {
            return;
        }

        var encoded = Uri.EscapeDataString(json);
        if (encoded.Length > MaxArgsHeaderLength)
        {
            return;
        }

        response.Headers[ArgsHeaderName] = encoded;
    }

    /// <summary>
    /// Reads back what <see cref="AttachArgs"/> wrote. Used by the round-trip test so the
    /// client's parse is pinned against this class's real encoding rather than a description
    /// of it; nothing on the request path calls it.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? DecodeArgs(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return MessageArgsJson.Deserialize(Uri.UnescapeDataString(encoded));
        }
        catch (UriFormatException)
        {
            // Not valid percent-encoding.
            return null;
        }
        catch (JsonException)
        {
            // Decoded, but not JSON. The deserialiser throws rather than returning null, so
            // the catch is what makes this a total function: a header this class cannot
            // read is a missing argument, never an exception, because the caller's answer
            // to a missing argument is the English body it already has.
            return null;
        }
    }
}
