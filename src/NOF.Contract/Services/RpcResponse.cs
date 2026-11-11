using NOF.Abstraction;
using System.Runtime.CompilerServices;

namespace NOF.Contract;

/// <summary>Adapts a transport failure to the statically known RPC outcome type.</summary>
public static class RpcResponse
{
    /// <summary>Accepts the exact declared response or constructs its failure case without reflection.</summary>
    public static T Require<T>(IUnion? response) where T : IRpcResponse<T>
    {
        if (response is T typed && typed.Value is not null)
        {
            return typed;
        }

        return response?.Value switch
        {
            Failure failure => T.CreateFailure(failure),
            null => throw new InvalidOperationException("The RPC call returned an uninitialized response."),
            _ => throw new InvalidOperationException($"The RPC response does not match '{typeof(T).FullName}'.")
        };
    }
}
