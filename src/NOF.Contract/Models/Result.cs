using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains a payload-free success or a business failure.</summary>
[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union Result(Success, Failure) : IRpcResponse<Result>
{
    /// <summary>Creates a payload-free success with an optional user-facing message.</summary>
    public static Result Success(string message = "") => new(new Success(message));

    /// <summary>Creates a success carrying data and an optional user-facing message.</summary>
    public static Result<T> Success<T>(T data, string message = "") => new(new Success<T>(data, message));

    /// <summary>Creates a business failure.</summary>
    public static Failure Fail(string code, string message) => new(code, message);

    /// <summary>Creates an initially successful stream. Enumeration failures remain stream failures.</summary>
    public static Result<IAsyncEnumerable<T>> Stream<T>(IAsyncEnumerable<T> data, string message = "") => Success(data, message);

    /// <summary>Creates a typed page result.</summary>
    public static Result<Page<T>> Page<T>(IEnumerable<T> items, int? totalCount = null, string message = "")
        => Success(new Page<T>(items.ToArray(), totalCount), message);

    /// <inheritdoc />
    public static Result CreateFailure(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure;
    }

    /// <inheritdoc />
    public static Result CreateEmptyResponse() => Success();
}
