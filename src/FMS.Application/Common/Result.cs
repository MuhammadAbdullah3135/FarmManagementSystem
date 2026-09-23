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

    public static Result<T> NotFound(string message = "Resource not found") =>
        Failure(Error.NotFound(message));

    public static Result<T> Validation(string message) =>
        Failure(Error.Validation(message));

    public static Result<T> Unauthorized(string message = "Unauthorized") =>
        Failure(Error.Unauthorized(message));

    public static Result<T> Conflict(string message) =>
        Failure(Error.Conflict(message));

    /// <summary>
    /// Existing state already means what the caller asked for: nothing was written and nothing
    /// is wrong. <see cref="Error.Superseded"/> explains why this is a distinct outcome.
    /// </summary>
    public static Result<T> Superseded(string message) =>
        Failure(Error.Superseded(message));

    public static Result<T> Unexpected(string message = "An unexpected error occurred") =>
        Failure(Error.Unexpected(message));
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

    public static Result NotFound(string message = "Resource not found") =>
        Failure(Error.NotFound(message));

    public static Result Validation(string message) =>
        Failure(Error.Validation(message));

    public static Result Unauthorized(string message = "Unauthorized") =>
        Failure(Error.Unauthorized(message));

    public static Result Conflict(string message) =>
        Failure(Error.Conflict(message));

    public static Result Unexpected(string message = "An unexpected error occurred") =>
        Failure(Error.Unexpected(message));
}
