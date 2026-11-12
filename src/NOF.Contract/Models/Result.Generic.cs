using NOF.Abstraction;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Contains the successful value itself or a shared business failure.</summary>
/// <typeparam name="T">The successful payload type.</typeparam>
[JsonUnion(TypeClassifier = typeof(ResultJsonTypeClassifier))]
public union Result<T>(T, Failure)
{
    /// <summary>Creates a result containing the specified business failure.</summary>
    public static Result<T> CreateFailure(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure;
    }

}
