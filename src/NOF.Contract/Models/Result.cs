using NOF.Abstraction;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains a payload-free success or a business failure.</summary>
[JsonUnion(TypeClassifier = typeof(ResultJsonTypeClassifier))]
public union Result(Empty, Failure)
{
    /// <summary>Creates an explicit payload-free success.</summary>
    public static Result Success() => new(Empty.Instance);

    /// <summary>Returns the successful value directly, without a wrapper.</summary>
    public static Result<T> Success<T>(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data);
    }

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
