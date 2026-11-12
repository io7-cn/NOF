using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

JsonSerializerOptions.ConfigureNOFJsonSerializerOptions(static options =>
    options.TypeInfoResolverChain.Add(SmokeJsonContext.Default));
var builder = NOFWebApplicationBuilder.Create(args);
builder.Services.AddApplicationService<OrderServer>();
builder.Services.AddApplicationService<PlainOrderService>();
builder.Services.AddRequestInboundMiddleware<ValidateReadOrder>();
await using var app = await builder.BuildAsync();
app.MapOpenApi();
app.Urls.Add("http://127.0.0.1:0");
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.ResolveDaemonServices();
    var preview = await OrderServer.Preview.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(preview.Label == "preview", "Operations sharing request/response types lost their identity");
    var plain = await PlainOrderService.Read.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(plain.Label == "local", "Local invocation failed");
    Expect(await PlainOrderService.Find.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty) is null,
        "Nullable local response was rejected");
    var decision = await PlainOrderService.Decide.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(decision is Failure { Code: "order.missing" }, "Business union invocation failed");
}
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
var id = Guid.NewGuid();
var savedJson = JsonSerializer.Serialize(Result.Success(), JsonSerializerOptions.NOF.GetRequiredTypeInfo<Result>());
Expect(savedJson == "{}", "Payload-free success serialized metadata");
Expect(JsonSerializer.Deserialize(savedJson, JsonSerializerOptions.NOF.GetRequiredTypeInfo<Result>()) is Empty,
    "Payload-free union did not round-trip without reflection");
using var update = await http.PutAsync($"/native/orders/{id}", new StringContent(
    $$"""{"id":"{{Guid.NewGuid()}}","state":"shipped"}""", Encoding.UTF8, "application/json"));
update.EnsureSuccessStatusCode();
var updated = await update.Content.ReadFromJsonAsync(JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderDto>());
Expect(updated == new OrderDto(id, "shipped", "default"), "Path identity/body binding did not survive AOT");
var read = await http.GetFromJsonAsync($"/native/orders/{id}?page=7", JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderDto>());
Expect(read == new OrderDto(id, "7", "read"), "Query binding or plain resource serialization failed");
using var nullable = await http.GetAsync($"/native/nullable/{id}");
Expect(nullable.StatusCode == HttpStatusCode.NoContent, "Nullable resource did not map to 204");
using var decisionResponse = await http.GetAsync($"/native/decisions/{id}");
Expect(decisionResponse.StatusCode == HttpStatusCode.NotFound, "Business union did not map to ProblemDetails");
var withMessage = await http.GetFromJsonAsync($"/native/messages/{id}", JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderReadReceipt>());
Expect(withMessage!.Order.Id == id && withMessage.Message == "Read from cache", "Success message was lost");
using var denied = await http.GetAsync($"/native/protected/{id}");
Expect(denied.StatusCode == HttpStatusCode.Unauthorized && denied.Headers.WwwAuthenticate.Count > 0,
    "Native endpoint lost application authorization");
using var invalid = await http.GetAsync($"/native/orders/{id}?page=-1");
Expect(invalid.StatusCode == HttpStatusCode.BadRequest, "Inbound policy rejection was lost");
using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
Expect(problem.RootElement.GetProperty("code").GetString() == "order.page", "Policy rejection details were lost");
using var failure = await http.PutAsJsonAsync($"/native/orders/{id}", new UpdateBody { State = "fail" }, JsonSerializerOptions.NOF.GetRequiredTypeInfo<UpdateBody>());
Expect(failure.StatusCode == HttpStatusCode.BadRequest, "Business failure was not mapped");
using var badBody = await http.PutAsync($"/native/orders/{id}", new StringContent("{}", Encoding.UTF8, "application/json"));
Expect(badBody.StatusCode == HttpStatusCode.BadRequest, "Required body member was omitted");
using var badPath = await http.GetAsync("/native/orders/invalid");
Expect(badPath.StatusCode == HttpStatusCode.NotFound, "Guid route constraint was ignored");
foreach (var path in new[] { "/Read", "/rpc", $"/orders/{id}" })
{
    using var missing = await http.GetAsync(path);
    Expect(missing.StatusCode == HttpStatusCode.NotFound, "An implicit legacy route was exposed");
}
using var openApi = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
var put = openApi.RootElement.GetProperty("paths").GetProperty("/native/orders/{id}").GetProperty("put");
var parameter = put.GetProperty("parameters").EnumerateArray().Single();
Expect(parameter.GetProperty("name").GetString() == "id", "OpenAPI omitted the path parameter");
var schema = put.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
if (schema.TryGetProperty("$ref", out var reference))
{
    schema = openApi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
}
var properties = schema.GetProperty("properties");
Expect(properties.TryGetProperty("state", out _) && !properties.TryGetProperty("id", out _), "OpenAPI leaked path identity into body");
await app.StopAsync();
Console.WriteLine("Native HTTP endpoint module smoke test passed.");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

public sealed record UpdateBody
{
    public required string State { get; init; }
}
public interface IOrderService : IApplicationService
{
    Result<OrderDto> Update(UpdateOrderRequest request);
    Result<OrderDto> Read(ReadOrderRequest request);
    OrderDto ReadValue(ReadOrderRequest request);
    OrderDto Preview(ReadOrderRequest request);
    OrderDto? Find(ReadOrderRequest request);
    PlainDecision Decide(ReadOrderRequest request);
    Result<OrderReadReceipt> ReadWithMessage(ReadOrderRequest request);
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
public sealed record OrderReadReceipt(OrderDto Order, string Message);

public partial class OrderServer : ApplicationService<IOrderService>;

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
    public override Task<Result<OrderDto>> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read")));
}

public sealed class ReadOrderValue : OrderServer.ReadValue
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read"));
}

public sealed class PreviewOrder : OrderServer.Preview
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, "preview", "preview"));
}

public sealed class FindOrder : OrderServer.Find
{
    public override Task<OrderDto?> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<OrderDto?>(null);
}

public sealed class ValidateReadOrder : IRequestInboundMiddleware
{
    public TopologyComparison Compare(IRequestInboundMiddleware other) => TopologyComparison.DoesNotMatter;

    public ValueTask InvokeAsync(RequestInboundContext context, object request, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        if (context.ServiceType == typeof(IOrderService)
            && context.ServiceMethodInfo.Name is nameof(IOrderService.Read) or nameof(IOrderService.ReadValue)
            && request is ReadOrderRequest { Page: < 0 })
        {
            context.ResponseHeaders[NOFInfrastructureConstants.Transport.Headers.HttpStatusCode] = "400";
            context.Reject(new Failure("order.page", "Page must be nonnegative"));
            return ValueTask.CompletedTask;
        }
        return next(context, request, cancellationToken);
    }
}

public interface IPlainOrderService : IApplicationService
{
    OrderDto Read(ReadOrderRequest request);
    OrderDto? Find(ReadOrderRequest request);
    PlainDecision Decide(ReadOrderRequest request);
}

[JsonUnion(TypeClassifier = typeof(ResultJsonTypeClassifier))]
public union PlainDecision(OrderDto, Failure);
public partial class PlainOrderService : ApplicationService<IPlainOrderService>;

public sealed class ReadPlainOrder : PlainOrderService.Read
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, "read", "local"));
}

public sealed class FindPlainOrder : PlainOrderService.Find
{
    public override Task<OrderDto?> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<OrderDto?>(null);
}

public sealed class DecidePlainOrder : PlainOrderService.Decide
{
    public override Task<PlainDecision> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(new Failure("order.missing", "Order not found"));
}

public sealed class DecideOrder : OrderServer.Decide
{
    public override Task<PlainDecision> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(new Failure("order.missing", "Order not found"));
}

public sealed class ReadProtectedOrderHandler : OrderServer.ReadProtected
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("An anonymous request must not reach the protected handler");
}

public sealed class ReadOrderWithMessageHandler : OrderServer.ReadWithMessage
{
    public override Task<Result<OrderReadReceipt>> HandleAsync(ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(new OrderReadReceipt(new OrderDto(request.Id, "cached", "read"), "Read from cache")));
}


[JsonSerializable(typeof(Result))]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(UpdateBody))]
[JsonSerializable(typeof(OrderReadReceipt))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
public partial class SmokeJsonContext : JsonSerializerContext;

[AutoInject(ServiceLifetime.Singleton)]
public sealed class SmokeEndpointModule : IHttpEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/native").AddEndpointFilter<ServiceRejectionHttpFilter>();
        group.MapGet("/orders/{id:guid}", ReadAsync);
        group.MapPut("/orders/{id:guid}", UpdateAsync);
        group.MapGet("/protected/{id:guid}", ReadProtectedAsync);
        group.MapGet("/nullable/{id:guid}", FindAsync);
        group.MapGet("/decisions/{id:guid}", DecideAsync);
        group.MapGet("/messages/{id:guid}", MessageAsync);
    }
    public static async Task<Ok<OrderDto>> ReadProtectedAsync(Guid id, HttpContext http, CancellationToken ct)
        => TypedResults.Ok((await http.InvokeAsync(OrderServer.ReadProtected.Descriptor, new ReadOrderRequest { Id = id }, ct)).Value as OrderDto
            ?? throw new InvalidOperationException("Protected handler returned no resource"));
    public static async Task<Results<Ok<OrderDto>, NoContent>> FindAsync(Guid id, HttpContext http, CancellationToken ct)
        => await http.InvokeAsync(PlainOrderService.Find.Descriptor, new ReadOrderRequest { Id = id }, ct) is { } value
            ? TypedResults.Ok(value) : TypedResults.NoContent();
    public static async Task<Results<Ok<OrderDto>, ProblemHttpResult>> DecideAsync(Guid id, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(PlainOrderService.Decide.Descriptor, new ReadOrderRequest { Id = id }, ct)).Value switch
        {
            OrderDto value => TypedResults.Ok(value),
            Failure failure => TypedResults.Problem(statusCode: 404, detail: failure.Message),
            _ => throw new InvalidOperationException("No outcome")
        };
    public static async Task<Ok<OrderReadReceipt>> MessageAsync(Guid id, HttpContext http, CancellationToken ct)
        => TypedResults.Ok((await http.InvokeAsync(OrderServer.ReadWithMessage.Descriptor, new ReadOrderRequest { Id = id }, ct)).Value as OrderReadReceipt
            ?? throw new InvalidOperationException("Message handler returned no resource"));
    public static async Task<Results<Ok<OrderDto>, ProblemHttpResult>> UpdateAsync(Guid id, UpdateBody body, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(OrderServer.Update.Descriptor,
            new UpdateOrderRequest { Id = id, Status = body.State }, ct)).Value switch
        {
            OrderDto value => TypedResults.Ok(value),
            Failure failure => TypedResults.Problem(statusCode: 400, detail: failure.Message),
            _ => throw new InvalidOperationException("No outcome")
        };
    public static async Task<Ok<OrderDto>> ReadAsync(Guid id, int? page, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OrderServer.ReadValue.Descriptor, new ReadOrderRequest { Id = id, Page = page }, ct));
}
