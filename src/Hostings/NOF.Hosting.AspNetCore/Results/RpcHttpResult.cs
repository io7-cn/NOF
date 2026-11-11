using Microsoft.AspNetCore.Http;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;
/// <summary>Writes an RPC result as an HTTP response.</summary>

public sealed class RpcHttpResult(
    object? rpcResult,
    int statusCode = StatusCodes.Status200OK) : Microsoft.AspNetCore.Http.IResult
{
    private readonly object? _rpcResult = rpcResult;

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Transport response bodies are framework-controlled runtime payloads.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Transport response bodies are framework-controlled runtime payloads.")]
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            _rpcResult,
            _rpcResult?.GetType() ?? typeof(object),
            cancellationToken: httpContext.RequestAborted).ConfigureAwait(false);
    }
}
/// <summary>Writes a streaming RPC result as an HTTP response.</summary>

[RequiresUnreferencedCode("Streaming HTTP response writing may require runtime JSON serialization for transport bodies.")]
[RequiresDynamicCode("Streaming HTTP response writing may require runtime JSON serialization for transport bodies.")]
public sealed class RpcStreamingHttpResult<TItem>(
    System.Runtime.CompilerServices.IUnion rpcResult,
    int statusCode) : Microsoft.AspNetCore.Http.IResult
{
    private readonly System.Runtime.CompilerServices.IUnion _rpcResult = rpcResult;

    /// <summary>Creates a streaming response with the default HTTP success status.</summary>
    public RpcStreamingHttpResult(Result<global::System.Collections.Generic.IAsyncEnumerable<TItem>> rpcResult)
        : this(rpcResult, StatusCodes.Status200OK)
    {
    }

    /// <summary>Creates a streaming response with an explicitly wrapped successful message.</summary>
    public RpcStreamingHttpResult(Result<Success<IAsyncEnumerable<TItem>>> rpcResult)
        : this(rpcResult, StatusCodes.Status200OK)
    {
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (_rpcResult.Value is Failure)
        {
            return new RpcHttpResult(_rpcResult, statusCode).ExecuteAsync(httpContext);
        }

        var success = RpcStreamResponse.Unwrap<TItem>(_rpcResult);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers[StreamingResponseMetadata.MessageHeader] = StreamingResponseMetadata.EncodeMessage(success.Message);
        return TypedResults.ServerSentEvents(success.Data).ExecuteAsync(httpContext);
    }
}
/// <summary>Writes a streaming RPC result using JSON-RPC.</summary>

public sealed class JsonRpcStreamingHttpResult<TItem>(
    System.Runtime.CompilerServices.IUnion rpcResult,
    ReadOnlyMemory<byte> requestId,
    IObjectSerializer serializer) : Microsoft.AspNetCore.Http.IResult
{
    private const string JsonRpcVersion = "2.0";
    private static readonly ReadOnlyMemory<byte> _dataPrefix = "data: "u8.ToArray();
    private static readonly ReadOnlyMemory<byte> _eventSeparator = "\n\n"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> _errorEvent = "event: error\n"u8.ToArray();
    private readonly System.Runtime.CompilerServices.IUnion _rpcResult = rpcResult;
    private readonly ReadOnlyMemory<byte> _requestId = requestId;
    private readonly IObjectSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var success = RpcStreamResponse.Unwrap<TItem>(_rpcResult);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers[StreamingResponseMetadata.MessageHeader] = StreamingResponseMetadata.EncodeMessage(success.Message);

        try
        {
            await foreach (var item in success.Data.WithCancellation(httpContext.RequestAborted).ConfigureAwait(false))
            {
                var itemPayload = item is null
                    ? "null"u8.ToArray()
                    : _serializer.Serialize(item, typeof(TItem));
                var envelope = CreateResultEnvelope(itemPayload);
                await WriteEventAsync(httpContext, envelope, isError: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            var envelope = CreateErrorEnvelope(-32603, "Internal error");
            await WriteEventAsync(httpContext, envelope, isError: true).ConfigureAwait(false);
        }
    }

    private byte[] CreateResultEnvelope(ReadOnlyMemory<byte> result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcVersion);
        writer.WritePropertyName("result");
        writer.WriteRawValue(result.Span, skipInputValidation: false);
        WriteRequestId(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private byte[] CreateErrorEnvelope(int code, string message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcVersion);
        writer.WritePropertyName("error");
        writer.WriteStartObject();
        writer.WriteNumber("code", code);
        writer.WriteString("message", message);
        writer.WriteEndObject();
        WriteRequestId(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private void WriteRequestId(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("id");
        writer.WriteRawValue(_requestId.Span, skipInputValidation: false);
    }

    private static async Task WriteEventAsync(HttpContext httpContext, ReadOnlyMemory<byte> envelope, bool isError)
    {
        if (isError)
        {
            await httpContext.Response.Body.WriteAsync(_errorEvent, httpContext.RequestAborted).ConfigureAwait(false);
        }

        await httpContext.Response.Body.WriteAsync(_dataPrefix, httpContext.RequestAborted).ConfigureAwait(false);
        await httpContext.Response.Body.WriteAsync(envelope, httpContext.RequestAborted).ConfigureAwait(false);
        await httpContext.Response.Body.WriteAsync(_eventSeparator, httpContext.RequestAborted).ConfigureAwait(false);
        await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
    }
}
