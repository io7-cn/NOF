using NOF.Application;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
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
builder.Services.AddScoped<OrderServer.ReadProtected, ReadProtectedOrderHandler>();

await using var app = await builder.BuildAsync();
app.MapOpenApi();
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();

using var http = new HttpClient(new InspectBodyHandler(new HttpClientHandler()))
{
    BaseAddress = new Uri(app.Urls.Single())
};
var client = new HttpOrderServiceClient(http);
var id = Guid.NewGuid();
var updated = await client.UpdateAsync(new UpdateOrderRequest { Id = id, Status = "shipped" }, Context.Empty);
Expect(updated.IsSuccess && updated.Value == new OrderDto(id, "shipped", "default"), "Generated PUT did not bind the path and body");

var read = await client.ReadAsync(new ReadOrderRequest { Id = id, Page = 7 }, Context.Empty);
Expect(read.IsSuccess && read.Value == new OrderDto(id, "7", "read"), "Generated GET did not bind the path and query");

using var raw = new HttpClient { BaseAddress = http.BaseAddress };
using var forged = await raw.PutAsync($"/orders/{id}",
    new StringContent($$"""{"id":"{{Guid.NewGuid()}}","state":"raw"}""", Encoding.UTF8, "application/json"));
forged.EnsureSuccessStatusCode();
var forgedResult = await JsonSerializer.DeserializeAsync(
    await forged.Content.ReadAsStreamAsync(), JsonSerializerOptions.NOF.GetRequiredTypeInfo<Result<OrderDto>>());
Expect(forgedResult?.Value == new OrderDto(id, "raw", "default"), "A JSON id changed the path ID");

using var badPath = await raw.GetAsync("/orders/invalid");
Expect(badPath.StatusCode == HttpStatusCode.BadRequest, "An invalid path value was accepted");
using var badBody = await raw.PutAsync($"/orders/{id}", new StringContent("{}", Encoding.UTF8, "application/json"));
Expect(badBody.StatusCode == HttpStatusCode.BadRequest, "A required body field was omitted");

using var denied = await raw.GetAsync($"/orders/{id}/protected");
Expect(denied.StatusCode == HttpStatusCode.Unauthorized && denied.Headers.WwwAuthenticate.Count > 0,
    "Generated route did not preserve NOF authorization");

var failure = await client.UpdateAsync(new UpdateOrderRequest { Id = id, Status = "fail" }, Context.Empty);
Expect(!failure.IsSuccess && failure.ErrorCode == "order.rejected", "Generated route did not serialize the business failure");

using var openApi = JsonDocument.Parse(await raw.GetStringAsync("/openapi/v1.json"));
var put = openApi.RootElement.GetProperty("paths").GetProperty("/orders/{id}").GetProperty("put");
var path = put.GetProperty("parameters").EnumerateArray().Single();
Expect(path.GetProperty("name").GetString() == "id" && path.GetProperty("in").GetString() == "path",
    "OpenAPI did not describe the path parameter");
var properties = put.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
    .GetProperty("schema").GetProperty("properties");
Expect(properties.TryGetProperty("state", out _) && !properties.TryGetProperty("id", out _),
    "OpenAPI did not describe the generated body");

await app.StopAsync();
Console.WriteLine("Generated HTTP route smoke test passed.");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[TransportOverHttp(HttpRpcStyle.ControllerRpc, "/orders")]
public interface IOrderService : IRpcService
{
    [HttpEndpoint(HttpVerb.Put, "/{id}")]
    Result<OrderDto> Update(UpdateOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/{id}")]
    Result<OrderDto> Read(ReadOrderRequest request);

    [HttpEndpoint(HttpVerb.Get, "/{id}/protected")]
    [RequirePermission("order.read")]
    Result<OrderDto> ReadProtected(ReadOrderRequest request);
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

public partial class OrderServer : RpcServer<IOrderService>;

public sealed class UpdateOrderHandler : OrderServer.Update
{
    public override Task<Result<OrderDto>> HandleAsync(
        UpdateOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<Result<OrderDto>>(request.Status == "fail"
            ? Result.Fail("order.rejected", "Order rejected")
            : Result.Success(new OrderDto(request.Id, request.Status, request.Label)));
}

public sealed class ReadOrderHandler : OrderServer.Read
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read")));
}

public sealed class ReadProtectedOrderHandler : OrderServer.ReadProtected
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("An anonymous request must not reach the protected handler");
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
[JsonSerializable(typeof(Result<OrderDto>))]
public partial class SmokeJsonContext : JsonSerializerContext;
