namespace FMS.Application.Common;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        Value = default;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static Result<T> NotFound(string message = "Resource not found", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.NotFound(message, messageKey, messageArgs));

    /// <summary>
    /// A validation failure. <paramref name="messageKey"/> is optional and purely additive:
    /// the English <paramref name="message"/> is unchanged, and a caller that renders keys
    /// can localise without the server choosing a language.
    /// </summary>
    public static Result<T> Validation(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Validation(message, messageKey, messageArgs));

    public static Result<T> Unauthorized(string message = "Unauthorized", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Unauthorized(message, messageKey, messageArgs));

    public static Result<T> Conflict(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Conflict(message, messageKey, messageArgs));

    /// <summary>
    /// Existing state already means what the caller asked for: nothing was written and nothing
    /// is wrong. <see cref="Error.Superseded"/> explains why this is a distinct outcome.
    /// </summary>
    public static Result<T> Superseded(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Superseded(message, messageKey, messageArgs));

    public static Result<T> Unexpected(string message = "An unexpected error occurred", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Unexpected(message, messageKey, messageArgs));
}

public class Result
{
    public bool IsSuccess { get; }
    public Error? Error { get; }

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    public static Result NotFound(string message = "Resource not found", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.NotFound(message, messageKey, messageArgs));

    public static Result Validation(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Validation(message, messageKey, messageArgs));

    public static Result Unauthorized(string message = "Unauthorized", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Unauthorized(message, messageKey, messageArgs));

    public static Result Conflict(string message, string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Conflict(message, messageKey, messageArgs));

    public static Result Unexpected(string message = "An unexpected error occurred", string? messageKey = null, IReadOnlyDictionary<string, object?>? messageArgs = null) =>
        Failure(Error.Unexpected(message, messageKey, messageArgs));
}
