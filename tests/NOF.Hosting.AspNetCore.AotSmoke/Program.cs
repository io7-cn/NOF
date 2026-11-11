using Microsoft.AspNetCore.Http.HttpResults;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

JsonSerializerOptions.ConfigureNOFJsonSerializerOptions(static options =>
    options.TypeInfoResolverChain.Add(SmokeJsonContext.Default));

var builder = NOFWebApplicationBuilder.Create(args);
builder.Services.AddRpcServer<OrderServer>();
builder.Services.AddScoped<OrderServer.Update, UpdateOrderHandler>();
builder.Services.AddScoped<OrderServer.Read, ReadOrderHandler>();
builder.Services.AddScoped<OrderServer.ReadWithMessage, ReadOrderWithMessageHandler>();
builder.Services.AddScoped<OrderServer.ReadProtected, ReadProtectedOrderHandler>();
builder.Services.AddScoped<OrderServer.Lookup, LookupOrderHandler>();
builder.Services.AddRequestHandler<ReadOrderRequest, OrderDto, ReadOrderUseCase>();
builder.Services.AddScoped<IRequestMiddleware<ReadOrderRequest, OrderDto>, ValidateReadOrder>();

await using var app = await builder.BuildAsync();
app.MapOpenApi();
app.MapGet("/native/orders/{id}", NativeReadOrderAsync);
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();

using var http = new HttpClient(new InspectBodyHandler(new HttpClientHandler()))
{
    BaseAddress = new Uri(app.Urls.Single())
};
var client = new HttpOrderServiceClient(http);
var id = Guid.NewGuid();
var updated = await client.UpdateAsync(new UpdateOrderRequest { Id = id, Status = "shipped" }, Context.Empty);
Expect(updated is OrderDto updatedSuccess && updatedSuccess == new OrderDto(id, "shipped", "default"), "Generated PUT did not bind the path and body");

var read = await client.ReadAsync(new ReadOrderRequest { Id = id, Page = 7 }, Context.Empty);
Expect(read is OrderDto readSuccess && readSuccess == new OrderDto(id, "7", "read"), "Generated GET did not bind the path and query");

var withMessage = await client.ReadWithMessageAsync(new ReadOrderRequest { Id = id }, Context.Empty);
Expect(withMessage is Success<OrderDto> messagePayload && messagePayload.Data.Id == id && messagePayload.Message == "Read from cache",
    "The optional success wrapper did not retain its data and message");

using var raw = new HttpClient { BaseAddress = http.BaseAddress };
using var native = await raw.GetAsync($"/native/orders/{id}?page=7");
native.EnsureSuccessStatusCode();
var nativeOrder = await JsonSerializer.DeserializeAsync(await native.Content.ReadAsStreamAsync(), JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderDto>());
Expect(nativeOrder == new OrderDto(id, "7", "read"), "Native HTTP did not return the use case's plain DTO");
using var nativeDenied = await raw.GetAsync($"/native/orders/{id}?page=-1");
Expect(nativeDenied.StatusCode == HttpStatusCode.BadRequest, "Native HTTP did not map an application policy rejection");
var rpcDenied = await client.ReadAsync(new ReadOrderRequest { Id = id, Page = -1 }, Context.Empty);
Expect(rpcDenied is Failure readFailure && readFailure.Code == "order.page", "RPC did not map the same use case policy rejection");
using var forged = await raw.PutAsync($"/orders/{id}",
    new StringContent($$"""{"id":"{{Guid.NewGuid()}}","state":"raw"}""", Encoding.UTF8, "application/json"));
forged.EnsureSuccessStatusCode();
var forgedResult = await JsonSerializer.DeserializeAsync(
    await forged.Content.ReadAsStreamAsync(), JsonSerializerOptions.NOF.GetRequiredTypeInfo<Result<OrderDto>>());
Expect(forgedResult is OrderDto forgedSuccess && forgedSuccess == new OrderDto(id, "raw", "default"), "A JSON id changed the path ID");

using var badPath = await raw.GetAsync("/orders/invalid");
Expect(badPath.StatusCode == HttpStatusCode.BadRequest, "An invalid path value was accepted");

var lookup = new LookupOrderRequest
{
    Slug = "café #1?",
    State = OrderState.Shipped,
    Day = new DateOnly(2026, 4, 10),
    Timestamp = new DateTime(2026, 4, 10, 9, 15, 0, DateTimeKind.Utc).AddTicks(1234),
    Amount = 12.5m,
    Warehouse = new WarehouseCode(17),
    Legacy = new LegacyOrderNumber(45),
    Price = new LegacyPrice(9.75m)
};
var culture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    var result = await client.LookupAsync(lookup, Context.Empty);
    Expect(result is string lookupSuccess && lookupSuccess == "café #1?|Shipped|2026-04-10|2026-04-10T09:15:00.0001234Z|12.5|17|45|9.75",
        "Generated GET did not round-trip scalar and custom path values");
}
finally
{
    CultureInfo.CurrentCulture = culture;
}

using var lowercaseEnum = await raw.GetAsync("/orders/lookup/slug/shipped/2026-04-10/2026-04-10T09:15:00Z/12.5/w-17/45/9.75");
lowercaseEnum.EnsureSuccessStatusCode();
using var badEnum = await raw.GetAsync("/orders/lookup/slug/invalid/2026-04-10/2026-04-10T09:15:00Z/12.5/w-17/45/9.75");
Expect(badEnum.StatusCode == HttpStatusCode.BadRequest, "An invalid enum path value was accepted");
using var badLegacy = await raw.GetAsync("/orders/lookup/slug/shipped/2026-04-10/2026-04-10T09:15:00Z/12.5/w-17/invalid/9.75");
Expect(badLegacy.StatusCode == HttpStatusCode.BadRequest, "An invalid static TryParse path value was accepted");

foreach (var invalidSlug in new string?[] { null, "", ".", "..", "a/b", "a\\b" })
{
    try
    {
        await client.LookupAsync(lookup with { Slug = invalidSlug }, Context.Empty);
        throw new InvalidOperationException("The generated client accepted an invalid path segment");
    }
    catch (ArgumentException exception) when (exception.ParamName == nameof(LookupOrderRequest.Slug))
    {
    }
}

using var badBody = await raw.PutAsync($"/orders/{id}", new StringContent("{}", Encoding.UTF8, "application/json"));
Expect(badBody.StatusCode == HttpStatusCode.BadRequest, "A required body field was omitted");

using var denied = await raw.GetAsync($"/orders/{id}/protected");
Expect(denied.StatusCode == HttpStatusCode.Unauthorized && denied.Headers.WwwAuthenticate.Count > 0,
    "Generated route did not preserve NOF authorization");

var failure = await client.UpdateAsync(new UpdateOrderRequest { Id = id, Status = "fail" }, Context.Empty);
Expect(failure is Failure rejected && rejected.Code == "order.rejected", "Generated route did not serialize the business failure");

using var openApi = JsonDocument.Parse(await raw.GetStringAsync("/openapi/v1.json"));
var put = openApi.RootElement.GetProperty("paths").GetProperty("/orders/{id}").GetProperty("put");
var path = put.GetProperty("parameters").EnumerateArray().Single();
Expect(path.GetProperty("name").GetString() == "id" && path.GetProperty("in").GetString() == "path",
    "OpenAPI did not describe the path parameter");
var properties = put.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
    .GetProperty("schema").GetProperty("properties");
Expect(properties.TryGetProperty("state", out _) && !properties.TryGetProperty("id", out _),
    "OpenAPI did not describe the generated body");
var lookupParameters = openApi.RootElement.GetProperty("paths")
    .GetProperty("/orders/lookup/{slug}/{state}/{day}/{timestamp}/{amount}/{warehouse}/{legacy}/{price}")
    .GetProperty("get").GetProperty("parameters");
var warehouse = lookupParameters.EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "warehouse");
Expect(warehouse.GetProperty("schema").GetProperty("type").GetString() == "string",
    "OpenAPI did not describe the custom path value as text");

await app.StopAsync();
Console.WriteLine("Generated HTTP route smoke test passed.");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<Results<Ok<OrderDto>, ProblemHttpResult>> NativeReadOrderAsync(
    Guid id, int? page, IRequestExecutor executor, CancellationToken cancellationToken)
{
    var execution = await executor.ExecuteAsync<ReadOrderRequest, OrderDto>(
        new ReadOrderRequest { Id = id, Page = page }, Context.Empty, cancellationToken);
    return execution switch
    {
        RequestCompleted<OrderDto> completed => TypedResults.Ok(completed.Response),
        RequestRejected rejected => TypedResults.Problem(
            statusCode: rejected.Kind switch
            {
                RequestRejectionKind.AuthenticationRequired => StatusCodes.Status401Unauthorized,
                RequestRejectionKind.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            }, title: rejected.Failure.Code, detail: rejected.Failure.Message),
        null => throw new InvalidOperationException("Missing execution outcome")
    };
}

[TransportOverHttp(HttpRpcStyle.ControllerRpc, "/orders")]
public interface IOrderService : IRpcService
{
    [HttpEndpoint(HttpVerb.Put, "/{id}")]
    Result<OrderDto> Update(UpdateOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/{id}")]
    Result<OrderDto> Read(ReadOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/{id}/with-message")]
    Result<Success<OrderDto>> ReadWithMessage(ReadOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/{id}/protected")]
    [RequirePermission("order.read")]
    Result<OrderDto> ReadProtected(ReadOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/lookup/{slug}/{state}/{day}/{timestamp}/{amount}/{warehouse}/{legacy}/{price}")]
    Result<string> Lookup(LookupOrderRequest request);
}

public sealed class UpdateOrderRequest
{
    public required Guid Id { get; init; }

    [JsonPropertyName("state")]
    public required string Status { get; init; }

    public string Label { get; set; } = "default";
}

public sealed class ReadOrderRequest
{
    public required Guid Id { get; init; }
    public int? Page { get; init; }
}

public sealed record OrderDto(Guid Id, string Status, string Label);

public sealed record LookupOrderRequest
{
    public required string? Slug { get; init; }
    public required OrderState State { get; init; }
    public required DateOnly Day { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal Amount { get; init; }
    public required WarehouseCode Warehouse { get; init; }
    public required LegacyOrderNumber Legacy { get; init; }
    public required LegacyPrice Price { get; init; }
}

public enum OrderState
{
    Pending,
    Shipped
}

public readonly record struct WarehouseCode(int Value) : IParsable<WarehouseCode>
{
    static WarehouseCode IParsable<WarehouseCode>.Parse(string text, IFormatProvider? provider)
        => text.StartsWith("w-", StringComparison.Ordinal)
            ? new(int.Parse(text.AsSpan(2), NumberStyles.None, provider))
            : throw new FormatException();

    static bool IParsable<WarehouseCode>.TryParse(string? text, IFormatProvider? provider, out WarehouseCode result)
    {
        result = default;
        if (text is null || !text.StartsWith("w-", StringComparison.Ordinal)
            || !int.TryParse(text.AsSpan(2), NumberStyles.None, provider, out var number))
        {
            return false;
        }

        result = new(number);
        return true;
    }

    public override string ToString() => FormattableString.Invariant($"w-{Value}");
}

public sealed record LegacyOrderNumber(int Value)
{
    public static bool TryParse(string text, out int result)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    public static bool TryParse(string text, out LegacyOrderNumber? result)
    {
        var success = TryParse(text, out int number);
        result = success ? new(number) : null;
        return success;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LegacyPrice(decimal Value) : IFormattable
{
    public static bool TryParse(string text, out LegacyPrice result)
        => TryParse(text, CultureInfo.GetCultureInfo("fr-FR"), out result);

    public static bool TryParse(string text, IFormatProvider? provider, out LegacyPrice result)
    {
        var success = decimal.TryParse(text, NumberStyles.AllowDecimalPoint, provider, out var value);
        result = new(value);
        return success;
    }

    public string ToString(string? format, IFormatProvider? provider) => Value.ToString(format, provider);
}

public partial class OrderServer : RpcServer<IOrderService>;

public sealed class UpdateOrderHandler : OrderServer.Update
{
    public override Task<Result<OrderDto>> HandleAsync(
        UpdateOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<Result<OrderDto>>(request.Status == "fail"
            ? Result.Fail("order.rejected", "Order rejected")
            : Result.Success(new OrderDto(request.Id, request.Status, request.Label)));
}

public sealed class ReadOrderHandler(IRequestExecutor executor) : OrderServer.Read
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => ExecuteUseCaseAsync<OrderDto>(executor, request, context, static dto => Result.Success(dto), cancellationToken);
}

public sealed class ReadOrderUseCase : RequestHandler<ReadOrderRequest, OrderDto>
{
    public override Task<OrderDto> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read"));
}

public sealed class ValidateReadOrder : IRequestMiddleware<ReadOrderRequest, OrderDto>
{
    public Task<RequestExecution<OrderDto>> InvokeAsync(ReadOrderRequest request, Context context,
        RequestExecutionDelegate<ReadOrderRequest, OrderDto> next, CancellationToken cancellationToken)
        => request.Page < 0
            ? Task.FromResult<RequestExecution<OrderDto>>(new RequestRejected(RequestRejectionKind.ValidationFailed,
                new Failure("order.page", "Page must be nonnegative")))
            : next(request, context, cancellationToken);
}

public sealed class ReadProtectedOrderHandler : OrderServer.ReadProtected
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("An anonymous request must not reach the protected handler");
}

public sealed class ReadOrderWithMessageHandler : OrderServer.ReadWithMessage
{
    public override Task<Result<Success<OrderDto>>> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(Result.WithMessage(new OrderDto(request.Id, "cached", "read"), "Read from cache"));
}

public sealed class LookupOrderHandler : OrderServer.Lookup
{
    public override Task<Result<string>> HandleAsync(
        LookupOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<string>(FormattableString.Invariant(
            $"{request.Slug}|{request.State}|{request.Day:yyyy-MM-dd}|{request.Timestamp:O}|{request.Amount}|{request.Warehouse.Value}|{request.Legacy.Value}|{request.Price.Value}")));
}

public sealed class InspectBodyHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Put)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (body.RootElement.TryGetProperty("id", out _) || !body.RootElement.TryGetProperty("state", out _))
            {
                throw new InvalidOperationException("The generated client sent an unexpected JSON body");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

[JsonSerializable(typeof(Result))]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Result<Success<OrderDto>>))]
[JsonSerializable(typeof(Result<OrderDto>))]
[JsonSerializable(typeof(Result<string>))]
public partial class SmokeJsonContext : JsonSerializerContext;
