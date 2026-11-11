using NOF.Abstraction;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains the successful value itself or a shared business failure.</summary>
/// <typeparam name="T">The successful payload type.</typeparam>
[JsonUnion(TypeClassifier = typeof(ResultJsonTypeClassifier))]
public union Result<T>(T, Failure) : IRpcResponse<Result<T>>
{
    /// <inheritdoc />
    public static Result<T> CreateFailure(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure;
    }

    /// <inheritdoc />
    public static Result<T> CreateEmptyResponse()
        => throw new InvalidOperationException($"A response of type '{typeof(Result<T>).FullName}' requires successful data.");
}
