using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Contract;

public static class HttpRpcTransportResultReader
{
    /// <summary>Reads an ordinary business response without requiring an outcome interface.</summary>
    public static async Task<T> ReadValueAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken, bool allowNull = false)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!response.IsSuccessStatusCode)
        {
            throw new ServiceRejectedException(await CreateTransportFailureAsync(response, cancellationToken).ConfigureAwait(false));
        }
        if (IsBodyEmpty(response))
        {
            if (allowNull && default(T) is null)
            {
                return default!;
            }
            throw new InvalidOperationException($"The HTTP response for '{typeof(T).FullName}' is empty.");
        }
        var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        if (value is null && (!allowNull || default(T) is not null))
        {
            throw new InvalidOperationException($"The HTTP response for '{typeof(T).FullName}' is empty or null.");
        }
        return value!;
    }

    public static async Task<Result<global::System.Collections.Generic.IAsyncEnumerable<TItem>>> ReadStreamingFallbackAsync<TItem>(
        HttpResponseMessage response,
        JsonTypeInfo<Result> fallbackTypeInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(fallbackTypeInfo);

        var fallbackResult = await ReadAsync(response, fallbackTypeInfo, cancellationToken).ConfigureAwait(false);
        return RpcResponse.Require<Result<global::System.Collections.Generic.IAsyncEnumerable<TItem>>>(fallbackResult);
    }

    public static async Task<T> ReadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> successTypeInfo,
        CancellationToken cancellationToken)
        where T : IRpcResponse<T>
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(successTypeInfo);

        if (response.IsSuccessStatusCode)
        {
            if (IsBodyEmpty(response))
            {
                return T.CreateEmptyResponse();
            }

            var payload = await HttpContentJsonExtensions.ReadFromJsonAsync(response.Content, successTypeInfo, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"HTTP RPC response body for '{typeof(T).FullName}' is empty.");
            if (payload.Value is null)
            {
                throw new InvalidOperationException("The HTTP RPC response contains no outcome case.");
            }

            return payload;
        }

        return await ReadFailureAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadFailureAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : IRpcResponse<T>
    {
        ArgumentNullException.ThrowIfNull(response);

        var failure = await CreateTransportFailureAsync(response, cancellationToken).ConfigureAwait(false);
        return T.CreateFailure(failure);
    }

    private static bool IsBodyEmpty(HttpResponseMessage response)
        => response.Content is null
            || response.Content.Headers.ContentLength == 0;

    private static async Task<string?> ReadTransportBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static async Task<Failure> CreateTransportFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadTransportBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (body is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.EnumerateObject().Count() == 3
                    && root.TryGetProperty("$kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.ValueEquals("failure")
                    && root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(code.GetString())
                    && root.TryGetProperty("message", out var description) && description.ValueKind == JsonValueKind.String)
                {
                    return new Failure(code.GetString()!, description.GetString()!);
                }
            }
            catch (JsonException)
            {
                // A non-contract HTTP error body remains diagnostic text for the transport failure.
            }
        }

        var message = body ?? response.ReasonPhrase ?? "HTTP RPC transport failure.";
        return Result.Fail(((int)response.StatusCode).ToString(), message);
    }

}
