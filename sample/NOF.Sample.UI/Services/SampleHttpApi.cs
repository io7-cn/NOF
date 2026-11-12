using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NOF.Sample.UI.Services;

/// <summary>Shared HTTP plumbing for the sample's explicitly maintained API integrations.</summary>
public abstract class SampleHttpApi(HttpClient httpClient)
{
    public static readonly HttpRequestOptionsKey<Context> ContextKey = new("NOF.Sample.ExecutionContext");

    protected Task<TResponse> SendOutcomeAsync<TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>(
        HttpMethod method, string uri, TRequest request, Context context, CancellationToken ct,
        Func<NOF.Abstraction.Failure, TResponse> createFailure)
        where TResponse : IUnion
        => SendAsync(method, uri, request, context,
            (response, token) => HttpRpcTransportResultReader.ReadAsync(response,
                JsonSerializerOptions.NOF.GetRequiredTypeInfo<TResponse>(), token, createFailure),
            ct);

    protected Task<Result> SendCommandAsync<TRequest>(HttpMethod method, string uri,
        TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context, static async (response, token) =>
            response.IsSuccessStatusCode ? Result.Success()
                : await HttpRpcTransportResultReader.ReadFailureAsync<Result>(response, token, static failure => failure), ct);

    protected Task<Result<TPayload>> SendResourceAsync<TRequest, TPayload>(HttpMethod method, string uri,
        TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context, static async (response, token) =>
            response.IsSuccessStatusCode
                ? (Result<TPayload>)await HttpRpcTransportResultReader.ReadValueAsync(response,
                    JsonSerializerOptions.NOF.GetRequiredTypeInfo<TPayload>(), token)
                : await HttpRpcTransportResultReader.ReadFailureAsync<Result<TPayload>>(response, token, static failure => failure), ct);

    protected Task<TResponse> SendValueAsync<TRequest, TResponse>(
        HttpMethod method, string uri, TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context,
            static (response, token) => HttpRpcTransportResultReader.ReadValueAsync(response,
                JsonSerializerOptions.NOF.GetRequiredTypeInfo<TResponse>(), token), ct);

    protected Task<Result<IAsyncEnumerable<TItem>>> SendStreamAsync<TRequest, TItem>(
        HttpMethod method, string uri, TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context,
            static async (response, token) =>
            {
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                {
                    return Result.Stream(SseResponseReader.ReadAsync(response,
                        JsonSerializerOptions.NOF.GetRequiredTypeInfo<TItem>(), token));
                }
                return await HttpRpcTransportResultReader.ReadFailureAsync<Result<IAsyncEnumerable<TItem>>>(response,
                    token, static failure => failure);
            }, ct, streaming: true);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string uri, TRequest request, Context context,
        Func<HttpResponseMessage, CancellationToken, Task<TResponse>> read,
        CancellationToken ct, bool streaming = false)
    {
        using var message = new HttpRequestMessage(method, uri);
        message.Options.Set(ContextKey, context);
        var tenantContext = context.TryGetItem(NOF.Abstraction.NOFAbstractionConstants.Transport.Headers.TenantId, out _)
            ? context : Context.Current;
        message.Headers.TryAddWithoutValidation(
            NOF.Abstraction.NOFAbstractionConstants.Transport.Headers.TenantId, tenantContext.TenantId);
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            message.Content = JsonContent.Create(request, JsonSerializerOptions.NOF.GetRequiredTypeInfo<TRequest>());
        }
        if (streaming)
        {
            message.Headers.Accept.ParseAdd("text/event-stream");
        }
        var response = await httpClient.SendAsync(message,
            streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
        var transferred = false;
        try
        {
            var result = await read(response, ct);
            transferred = streaming && response.IsSuccessStatusCode
                && response.Content.Headers.ContentType?.MediaType == "text/event-stream";
            return result;
        }
        finally
        {
            if (!transferred)
            {
                response.Dispose();
            }
        }
    }

    protected static string Query(string uri, params (string Name, object? Value)[] values)
        => uri + "?" + string.Join("&", values.Where(static item => item.Value is not null)
            .Select(static item => Uri.EscapeDataString(item.Name) + "="
                + Uri.EscapeDataString(Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? "")));
}
