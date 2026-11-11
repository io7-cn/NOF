using NOF.Abstraction;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Contract;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class JsonRpcHttpClient
{
    private const string JsonRpcVersion = "2.0";

    /// <summary>Invokes a JSON-RPC adapter for an ordinary application response.</summary>
    public static async Task<TResponse> SendValueAsync<TRequest, TResponse>(
        HttpClient httpClient, string endpoint, string method, TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers, JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken, bool allowNull = false)
    {
        ValidateRequestArguments(httpClient, endpoint, method, request, headers, requestTypeInfo);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
        var id = Guid.NewGuid().ToString("N");
        using var httpRequest = CreateHttpRequest(endpoint, id, method, request, headers, requestTypeInfo);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return await HttpRpcTransportResultReader.ReadValueAsync(response, responseTypeInfo, cancellationToken, allowNull).ConfigureAwait(false);
        }
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateResponseEnvelope(root, id);
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            throw new ServiceRejectedException(ReadError(error));
        }
        if (!root.TryGetProperty("result", out var result))
        {
            throw new InvalidOperationException("JSON-RPC response must contain either a result or an error object.");
        }
        var value = result.Deserialize(responseTypeInfo);
        if (value is null && (!allowNull || default(TResponse) is not null))
        {
            throw new InvalidOperationException($"The JSON-RPC response for '{typeof(TResponse).FullName}' is null.");
        }
        return value!;
    }

    public static async Task<TResponse> SendAsync<
        TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>(
        HttpClient httpClient,
        string endpoint,
        string method,
        TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
        where TResponse : IRpcResponse<TResponse>
    {
        ValidateRequestArguments(httpClient, endpoint, method, request, headers, requestTypeInfo);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);

        var id = Guid.NewGuid().ToString("N");
        using var httpRequest = CreateHttpRequest(endpoint, id, method, request, headers, requestTypeInfo);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return await HttpRpcTransportResultReader.ReadFailureAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
        }

        return await ReadResponseEnvelopeAsync(response, id, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    public static Task<Result<IAsyncEnumerable<TItem>>> SendStreamingAsync<TRequest, TItem>(
        HttpClient httpClient, string endpoint, string method, TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers, JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TItem> itemTypeInfo, JsonTypeInfo<Result> fallbackTypeInfo, CancellationToken cancellationToken)
        => SendStreamingCoreAsync<TRequest, TItem, Result<IAsyncEnumerable<TItem>>>(httpClient, endpoint, method,
            request, headers, requestTypeInfo, itemTypeInfo, fallbackTypeInfo,
            static (items, _) => Result.Stream(items), cancellationToken);

    /// <summary>Reads an SSE stream whose successful payload explicitly carries a message.</summary>
    public static Task<Result<Success<IAsyncEnumerable<TItem>>>> SendStreamingWithMessageAsync<TRequest, TItem>(
        HttpClient httpClient, string endpoint, string method, TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers, JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TItem> itemTypeInfo, JsonTypeInfo<Result> fallbackTypeInfo, CancellationToken cancellationToken)
        => SendStreamingCoreAsync<TRequest, TItem, Result<Success<IAsyncEnumerable<TItem>>>>(httpClient, endpoint, method,
            request, headers, requestTypeInfo, itemTypeInfo, fallbackTypeInfo,
            static (items, message) => Result.WithMessage(items, message), cancellationToken);

    private static async Task<TResponse> SendStreamingCoreAsync<TRequest, TItem, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>(
        HttpClient httpClient, string endpoint, string method, TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers, JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TItem> itemTypeInfo, JsonTypeInfo<Result> fallbackTypeInfo,
        Func<IAsyncEnumerable<TItem>, string, TResponse> createSuccess, CancellationToken cancellationToken)
        where TResponse : IRpcResponse<TResponse>
    {
        ValidateRequestArguments(httpClient, endpoint, method, request, headers, requestTypeInfo);
        ArgumentNullException.ThrowIfNull(itemTypeInfo);
        ArgumentNullException.ThrowIfNull(fallbackTypeInfo);

        var id = Guid.NewGuid().ToString("N");
        using var httpRequest = CreateHttpRequest(endpoint, id, method, request, headers, requestTypeInfo);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                return await HttpRpcTransportResultReader.ReadFailureAsync<TResponse>(
                    response,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "text/event-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            var stream = ReadStreamingItemsAsync(response, id, itemTypeInfo, cancellationToken);
            return createSuccess(stream, StreamingResponseMetadata.ReadMessage(response));
        }

        using (response)
        {
            var fallback = await ReadResponseEnvelopeAsync(response, id, fallbackTypeInfo, cancellationToken).ConfigureAwait(false);
            return RpcResponse.Require<TResponse>(fallback);
        }
    }

    private static async Task<TResponse> ReadResponseEnvelopeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>(
        HttpResponseMessage response,
        string expectedId,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
        where TResponse : IRpcResponse<TResponse>
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateResponseEnvelope(root, expectedId);

        if (root.TryGetProperty("result", out var resultElement))
        {
            var result = resultElement.Deserialize(responseTypeInfo)
                ?? throw new InvalidOperationException($"JSON-RPC response result for '{typeof(TResponse).FullName}' is null.");
            if (result.Value is null)
            {
                throw new InvalidOperationException("The JSON-RPC response contains no outcome case.");
            }

            return result;
        }

        if (root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.Object)
        {
            return TResponse.CreateFailure(ReadError(errorElement));
        }

        throw new InvalidOperationException("JSON-RPC response must contain either a result or an error object.");
    }

    private static async IAsyncEnumerable<TItem> ReadStreamingItemsAsync<TItem>(
        HttpResponseMessage response,
        string expectedId,
        JsonTypeInfo<TItem> itemTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stringTypeInfo = JsonSerializerOptions.NOF.GetRequiredTypeInfo<string>();
        await foreach (var payload in SseResponseReader.ReadAsync<string>(
                           response,
                           stringTypeInfo,
                           cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            ValidateResponseEnvelope(root, expectedId);

            if (root.TryGetProperty("result", out var resultElement))
            {
                yield return resultElement.Deserialize(itemTypeInfo)
                    ?? throw new InvalidOperationException($"JSON-RPC streaming result for '{typeof(TItem).FullName}' is null.");
                continue;
            }

            if (root.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.Object)
            {
                var error = ReadError(errorElement);
                throw new InvalidOperationException($"JSON-RPC streaming error {error.Code}: {error.Message}");
            }

            throw new InvalidOperationException("JSON-RPC streaming response must contain either a result or an error object.");
        }
    }

    private static HttpRequestMessage CreateHttpRequest<TRequest>(
        string endpoint,
        string id,
        string method,
        TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers,
        JsonTypeInfo<TRequest> requestTypeInfo)
    {
        var requestPayload = CreateRequestPayload(id, method, request, requestTypeInfo);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(requestPayload)
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        ApplyHeaders(httpRequest, headers);
        return httpRequest;
    }

    private static void ValidateRequestArguments<TRequest>(
        HttpClient httpClient,
        string endpoint,
        string method,
        TRequest request,
        IEnumerable<KeyValuePair<string, string?>> headers,
        JsonTypeInfo<TRequest> requestTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
    }

    private static byte[] CreateRequestPayload<TRequest>(
        string id,
        string method,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcVersion);
        writer.WriteString("method", method);
        writer.WritePropertyName("params");
        JsonSerializer.Serialize(writer, request, requestTypeInfo);
        writer.WriteString("id", id);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void ApplyHeaders(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string?>> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (value is null)
            {
                continue;
            }

            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static Failure ReadError(JsonElement errorElement)
    {
        var code = errorElement.TryGetProperty("code", out var codeElement)
            ? codeElement.ToString()
            : "-32603";
        if (errorElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("code", out var applicationCode) && applicationCode.ValueKind == JsonValueKind.String)
        {
            code = applicationCode.GetString() ?? code;
        }
        var message = errorElement.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? "JSON-RPC error"
                : "JSON-RPC error";
        return Result.Fail(code, message);
    }

    private static void ValidateResponseEnvelope(JsonElement root, string expectedId)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String
            || !string.Equals(version.GetString(), JsonRpcVersion, StringComparison.Ordinal)
            || !root.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || !string.Equals(id.GetString(), expectedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid JSON-RPC response envelope.");
        }
    }
}
