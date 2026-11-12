using NOF.Abstraction;
using NOF.Contract;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NOF.Sample.UI.Services;

/// <summary>Shared HTTP plumbing for the sample's explicitly maintained API integrations.</summary>
public abstract class SampleHttpApi(HttpClient httpClient)
{
    public static readonly HttpRequestOptionsKey<Context> ContextKey = new("NOF.Sample.ExecutionContext");

    protected Task<Result> SendCommandAsync<TRequest>(HttpMethod method, string uri,
        TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context, static async (response, token) =>
            response.IsSuccessStatusCode ? Result.Success()
                : (Result)await ReadProblemAsync(response, token), ct);

    protected Task<Result<TPayload>> SendResourceAsync<TRequest, TPayload>(HttpMethod method, string uri,
        TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context, static async (response, token) =>
            response.IsSuccessStatusCode
                ? (Result<TPayload>)await ReadResourceAsync<TPayload>(response, token)
                : (Result<TPayload>)await ReadProblemAsync(response, token), ct);

    protected Task<TResponse> SendValueAsync<TRequest, TResponse>(
        HttpMethod method, string uri, TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context,
            static (response, token) => ReadResourceAsync<TResponse>(response, token), ct);

    protected Task<Result<IAsyncEnumerable<TItem>>> SendStreamAsync<TRequest, TItem>(
        HttpMethod method, string uri, TRequest request, Context context, CancellationToken ct)
        => SendAsync(method, uri, request, context,
            static async (response, token) =>
            {
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                {
                    return Result.Stream(ReadEventsAsync<TItem>(response, token));
                }
                if (response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Expected a native SSE response.");
                }
                return await ReadProblemAsync(response, token);
            }, ct, streaming: true);

    private static async Task<T> ReadResourceAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ServiceRejectedException(await ReadProblemAsync(response, ct));
        }
        return await response.Content.ReadFromJsonAsync(JsonSerializerOptions.NOF.GetRequiredTypeInfo<T>(), ct)
            ?? throw new InvalidOperationException("The resource response was empty.");
    }

    private static async Task<Failure> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var fallback = new Failure(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            response.ReasonPhrase ?? "HTTP request failed");
        if (response.Content.Headers.ContentLength == 0)
        {
            return fallback;
        }
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = problem.RootElement;
            var code = root.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : fallback.Code;
            var detail = root.TryGetProperty("detail", out value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : root.TryGetProperty("title", out value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : fallback.Message;
            return new Failure(string.IsNullOrWhiteSpace(code) ? fallback.Code : code, detail ?? fallback.Message);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static async IAsyncEnumerable<T> ReadEventsAsync<T>(HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await foreach (var item in SseParser.Create(stream).EnumerateAsync(ct))
            {
                yield return JsonSerializer.Deserialize(item.Data, JsonSerializerOptions.NOF.GetRequiredTypeInfo<T>())
                    ?? throw new InvalidOperationException("The event payload was null.");
            }
        }
    }

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
