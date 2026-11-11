using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NOF.Hosting.AspNetCore;

/// <summary>Recognizes bare and explicitly message-wrapped RPC streams.</summary>
public static class RpcStreamResponse
{
    /// <summary>Finds the stream item type in a declared response union.</summary>
    public static bool TryGetItemType(Type responseType, [NotNullWhen(true)] out Type? itemType)
    {
        itemType = null;
        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            return false;
        }
        var payload = responseType.GetGenericArguments()[0];
        if (payload.IsGenericType && payload.GetGenericTypeDefinition() == typeof(Success<>))
        {
            payload = payload.GetGenericArguments()[0];
        }
        if (!payload.IsGenericType || payload.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
        {
            return false;
        }
        itemType = payload.GetGenericArguments()[0];
        return true;
    }

    /// <summary>Extracts an already selected successful stream and its optional message.</summary>
    public static (IAsyncEnumerable<T> Data, string Message) Unwrap<T>(IUnion response) => response.Value switch
    {
        IAsyncEnumerable<T> data => (data, string.Empty),
        Success<IAsyncEnumerable<T>> success => (success.Data, success.Message),
        _ => throw new InvalidOperationException("The response does not contain a successful stream.")
    };
}
