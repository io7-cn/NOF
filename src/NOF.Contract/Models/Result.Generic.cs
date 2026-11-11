using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains a typed success or a business failure. Match the cases to access their data.</summary>
/// <typeparam name="T">The successful payload type.</typeparam>
[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union Result<T>(Success<T>, Failure) : IRpcResponse<Result<T>>
{
    /// <summary>Wraps a non-null payload in a successful result.</summary>
    public static implicit operator Result<T>(T data) => new Success<T>(data);

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
