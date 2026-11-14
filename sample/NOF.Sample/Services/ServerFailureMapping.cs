using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Adapts a predictable failure to the UI's explicit port.</summary>
public static class ServerFailureMapping
{
    public static Failure From(IFailure failure) => new(failure.Title, failure.Detail);

    public static async Task<UiResponse> Command(Task<object?> invocation)
        => InvocationResult.Value(await invocation) switch
        {
            IFailure failure => From(failure),
            null => throw new InvalidOperationException("The command returned no value."),
            _ => Empty.Instance
        };

    public static async Task<UiResponse<T>> Resource<T>(Task<object?> invocation)
        => InvocationResult.Value(await invocation) switch
        {
            IFailure failure => From(failure),
            T value => value,
            _ => throw new InvalidOperationException("The operation returned an unexpected resource.")
        };
}
