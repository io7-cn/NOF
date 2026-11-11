using NOF.Abstraction;
using System.Runtime.CompilerServices;

namespace NOF.Contract;

/// <summary>Provides compile-time transport construction for a concrete RPC outcome union.</summary>
/// <typeparam name="TSelf">The concrete outcome type.</typeparam>
public interface IRpcResponse<TSelf> : IUnion where TSelf : IRpcResponse<TSelf>
{
    /// <summary>Constructs a typed failure without reflection or runtime generic activation.</summary>
    static abstract TSelf CreateFailure(Failure failure);

    /// <summary>Handles an empty successful response; payload-requiring outcomes reject it.</summary>
    static abstract TSelf CreateEmptyResponse();
}
