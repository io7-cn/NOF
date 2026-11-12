using NOF.Abstraction;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains a payload-free success or a business failure.</summary>
[JsonUnion(TypeClassifier = typeof(ResultJsonTypeClassifier))]
public union Result(Success, Failure)
{
    /// <summary>Creates a payload-free success with an optional user-facing message.</summary>
    public static Result Success(string message = "") => new(new Success(message));

    /// <summary>Returns the successful value directly, without a wrapper.</summary>
    public static Result<T> Success<T>(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data);
    }

    /// <summary>Creates an explicitly wrapped payload with a successful message.</summary>
    public static Result<Success<T>> WithMessage<T>(T data, string message) => new Success<T>(data, message);

    /// <summary>Creates a business failure.</summary>
    public static Failure Fail(string code, string message) => new(code, message);

    /// <summary>Returns an existing shared failure after validating it.</summary>
    public static Failure Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure;
    }

    /// <summary>Creates an initially successful stream. Enumeration failures remain stream failures.</summary>
    public static Result<IAsyncEnumerable<T>> Stream<T>(IAsyncEnumerable<T> data) => Success(data);

    /// <summary>Creates a typed page result.</summary>
    public static Result<Page<T>> Page<T>(IEnumerable<T> items, int? totalCount = null)
        => Success(new Page<T>(items.ToArray(), totalCount));

    /// <summary>Creates a result containing the specified business failure.</summary>
    public static Result CreateFailure(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure;
    }

}
