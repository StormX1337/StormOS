using System.Diagnostics.CodeAnalysis;

namespace StormOS.Core.Common;

/// <summary>Represents the outcome of an operation that does not return a value.</summary>
public class Result
{
    /// <summary>Initializes a new instance of the <see cref="Result"/> class.</summary>
    /// <param name="error">The error, or <see langword="null"/> on success.</param>
    protected Result(StormError? error) => Error = error;

    /// <summary>Gets the error when the operation failed.</summary>
    public StormError? Error { get; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets a successful result.</summary>
    public static Result Success { get; } = new(null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(StormError error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Creates a failed result.</summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Friendly message.</param>
    /// <param name="detail">Technical detail.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(string code, string message, string? detail = null) => new(new StormError(code, message, detail));
}

/// <summary>Represents the outcome of an operation that returns a value.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Result<T> : Result
{
    private Result(T? value, StormError? error)
        : base(error) => Value = value;

    /// <summary>Gets the value when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Fail(StormError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Creates a failed result.</summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Friendly message.</param>
    /// <param name="detail">Technical detail.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Fail(string code, string message, string? detail = null) => new(default, new StormError(code, message, detail));

    /// <summary>Returns the value or throws when the result is a failure.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T GetValueOrThrow() =>
        IsSuccess ? Value! : throw new InvalidOperationException(Error.ToString());
}
