using Microsoft.AspNetCore.Builder;
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
Expect(NOF.Sample.ConfigNodeName.TryFrom("node", out var nodeName), "Valid value object was rejected");
Expect(!NOF.Sample.ConfigNodeName.TryFrom(new string('x', 101), out _), "Length rule was skipped by TryFrom");
Expect(!NOF.Sample.ConfigNodeName.TryFrom(" ", out _, out var nameError) && nameError == "Node name cannot be blank.",
    "Custom IsValid message was lost by TryFrom");
var nameJson = JsonSerializer.Serialize(nodeName, JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.ConfigNodeName>());
Expect((string)JsonSerializer.Deserialize(nameJson, JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.ConfigNodeName>()) == "node",
    "Value-object From conversion did not round-trip without reflection");
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.AddApplicationPart(typeof(SmokeEndpointModule).Assembly);
builder.Services.AddApplicationService<OrderServer>();
builder.Services.AddApplicationService<PlainOrderService>();
builder.Services.AddApplicationService<ExpectedOrderService>();
builder.Services.AddInvocationFilters(filters =>
{
    filters.Global.Use<ValidateReadOrder>();
    filters.Service<IExpectedOrderService>().Exclude<ValidateReadOrder>();
    filters.UseCase(ExpectedOrderService.Evaluate.Descriptor).Use<ValidateReadOrder>(5);
});
builder.Services.AddSingleton<SmokeOutboxStore>();
builder.Services.AddSingleton<IOutboxStore>(provider => provider.GetRequiredService<SmokeOutboxStore>());
builder.Services.AddOutbox(outbox => outbox.Add<SmokeIntent, SmokeIntentHandler>("smoke.intent", 1));
await using var app = builder.Build();
app.UseNOF();
SmokeEndpointModule.Map(app);
app.MapOpenApi();
using (var scope = app.Services.CreateScope())
{
    using var binding = scope.ServiceProvider.BindNOF(Context.Empty);
    var messageId = await scope.ServiceProvider.GetRequiredService<IOutbox>()
        .EnqueueAsync(new SmokeIntent("typed"), Context.Empty);
    var record = app.Services.GetRequiredService<SmokeOutboxStore>().Record
        ?? throw new InvalidOperationException("The outbox did not stage its snapshot");
    Expect(record.Id == messageId && record.MessageType == "smoke.intent" && record.SchemaVersion == 1,
        "Outbox identity or schema was lost");
    await scope.ServiceProvider.GetRequiredService<OutboxRegistry>().Get(record.MessageType, record.SchemaVersion)
        .Deliver(scope.ServiceProvider, record, new OutboxDeliveryContext
        {
            MessageId = record.Id,
            Context = Context.Empty,
            Attempt = 1,
            Headers = new Dictionary<string, string?>()
        }, CancellationToken.None);
    Expect(SmokeIntentHandler.Delivered == "typed", "The statically registered outbox handler did not run");
}
app.Urls.Add("http://127.0.0.1:0");
using (var scope = app.Services.CreateScope())
{
    using var binding = scope.ServiceProvider.BindNOF(Context.Empty);
    var plan = scope.ServiceProvider.GetRequiredService<IInvocationPlanProvider>()
        .Describe(OrderServer.Read.Descriptor, InvocationOrigin.Http);
    Expect(plan.FailureCases.Any(item => item.CaseType == typeof(ValidateReadOrder.InvalidPage)),
        "Generated filter catalog was not captured without reflection");
    Expect(ValidateReadOrder.FailureCases.Single().Title == "order.page", "Filter error code was lost");
    var preview = await OrderServer.Preview.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(preview is OrderDto { Label: "preview" }, "Operations sharing request/response types lost their identity");
    var plain = await PlainOrderService.Read.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(plain is OrderDto { Label: "local" }, "Local invocation failed");
    Expect(await PlainOrderService.Find.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty) is null,
        "Nullable local response was rejected");
    var decision = await PlainOrderService.Decide.InvokeAsync(scope.ServiceProvider, new ReadOrderRequest { Id = Guid.Empty }, Context.Empty);
    Expect(InvocationResult.Value(decision) is Failure { Title: "order.missing" }, "Business union invocation failed");
    var missing = await ExpectedOrderService.Evaluate.InvokeAsync(scope.ServiceProvider,
        new ReadOrderRequest { Id = Guid.Empty, Page = -1 }, Context.Empty);
    Expect(InvocationResult.Value(missing) is ExpectedOrderService.Evaluate.Missing, "Declared expected failure was lost");
    var conflict = await ExpectedOrderService.Evaluate.InvokeAsync(scope.ServiceProvider,
        new ReadOrderRequest { Id = Guid.Empty, Page = 0 }, Context.Empty);
    Expect(InvocationResult.Value(conflict) is ExpectedOrderService.Evaluate.Conflict { Error.Id: var conflictId } && conflictId == Guid.Empty,
        "Typed expected failure data was lost");
}
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
var id = Guid.NewGuid();
Expect(typeof(ExpectedOrderService.Evaluate.Missing).DeclaringType == typeof(ExpectedOrderService.Evaluate),
    "Inherited failure types changed Contract identity");
Expect(typeof(ExpectedOrderService.Evaluate.Result).Assembly == typeof(ExpectedOrderService).Assembly,
    "Generated result is not owned by Application");
Expect(new ExpectedOrderService.Evaluate.Missing("factory").GetType() == typeof(ExpectedOrderService.Evaluate.Missing),
    "Inherited constructor did not return the canonical failure type");
var conflictData = new OrderConflict(id);
Expect(ReferenceEquals(conflictData, new ExpectedOrderService.Evaluate.Conflict(conflictData).Error),
    "Inherited constructor replaced error data");
Expect(new ExpectedOrderService.Evaluate.Missing().Title == "404", "Inherited failure lost its declared Title");
var savedJson = JsonSerializer.Serialize(Empty.Instance, JsonSerializerOptions.NOF.GetRequiredTypeInfo<Empty>());
Expect(savedJson == "{}", "Payload-free success serialized metadata");
Expect(JsonSerializer.Deserialize(savedJson, JsonSerializerOptions.NOF.GetRequiredTypeInfo<Empty>()) is Empty,
    "Payload-free union did not round-trip without reflection");
using var update = await http.PutAsync($"/native/orders/{id}", new StringContent(
    $$"""{"id":"{{Guid.NewGuid()}}","state":"shipped"}""", Encoding.UTF8, "application/json"));
update.EnsureSuccessStatusCode();
var updated = await update.Content.ReadFromJsonAsync(JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderDto>());
Expect(updated == new OrderDto(id, "shipped", "default"), "Path identity/body binding did not survive AOT");
var read = await http.GetFromJsonAsync($"/native/orders/{id}?page=7", JsonSerializerOptions.NOF.GetRequiredTypeInfo<OrderDto>());
Expect(read == new OrderDto(id, "7", "read"), "Query binding or plain resource serialization failed");
using var nullable = await http.GetAsync($"/native/nullable/{id}");
Expect(nullable.StatusCode == HttpStatusCode.OK && await nullable.Content.ReadAsStringAsync() == "null", "Nullable resource did not map to JSON null");
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
Expect(problem.RootElement.GetProperty("title").GetString() == "order.page", "Policy rejection details were lost");
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
using var expectedMissing = await http.GetAsync($"/native/expected/{id}?page=-1");
Expect(expectedMissing.StatusCode == HttpStatusCode.NotFound, "Declared missing case did not map to 404");
using var expectedConflict = await http.GetAsync($"/native/expected/{id}?page=0");
Expect(expectedConflict.StatusCode == HttpStatusCode.Conflict, "Declared typed conflict did not map to 409");
NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result deleteOutcome =
    new NOF.Sample.Application.NOFSampleService.DeleteConfigNode.HasChildren(new NOF.Sample.NodeHasChildrenError(42));
var deleteJson = JsonSerializer.Serialize(deleteOutcome,
    JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result>());
var restoredDelete = JsonSerializer.Deserialize(deleteJson,
    JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result>());
Expect(restoredDelete.Match(_ => 0L, _ => -1L, error => error.Error.NodeId) == 42,
    "Source-generated expected-outcome metadata did not round-trip custom failure data without reflection");
deleteOutcome = new NOF.Sample.Application.NOFSampleService.DeleteConfigNode.NotFound("Missing node");
var missingJson = JsonSerializer.Serialize(deleteOutcome,
    JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result>());
var restoredMissing = JsonSerializer.Deserialize(missingJson,
    JsonSerializerOptions.NOF.GetRequiredTypeInfo<NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result>());
Expect(restoredMissing.Match(_ => "success", error => error.Detail, _ => "children") == "Missing node",
    "Source-generated expected-outcome metadata lost default Error data");
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
    PlainDecision Update(UpdateOrderRequest request);
    PlainDecision Read(ReadOrderRequest request);
    OrderDto ReadValue(ReadOrderRequest request);
    OrderDto Preview(ReadOrderRequest request);
    OrderDto? Find(ReadOrderRequest request);
    PlainDecision Decide(ReadOrderRequest request);
    OrderReceipt ReadWithMessage(ReadOrderRequest request);
    [RequirePermission("order.read")]
    PlainDecision ReadProtected(ReadOrderRequest request);
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
    public override Task<PlainDecision> HandleAsync(
        UpdateOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(request.Status == "fail"
            ? new Failure("order.rejected", "Order rejected")
            : new OrderDto(request.Id, request.Status, request.Label));
}

public sealed class ReadOrderHandler : OrderServer.Read
{
    public override Task<PlainDecision> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read"));
}

public sealed class ReadOrderValue : OrderServer.ReadValue
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, request.Page?.ToString() ?? "", "read"));
}

public sealed class PreviewOrder : OrderServer.Preview
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, "preview", "preview"));
}

public sealed class FindOrder : OrderServer.Find
{
    public override Task<OrderDto?> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<OrderDto?>(null);
}

[FailureCase("InvalidPage", "order.page")]
public sealed partial class ValidateReadOrder : IInvocationFilter
{
    public ValueTask<object?> InvokeAsync(InvocationContext context, object request, InvocationDelegate next, CancellationToken cancellationToken)
    {
        if (context.ServiceType == typeof(IOrderService)
            && context.ServiceMethodInfo.Name is nameof(IOrderService.Read) or nameof(IOrderService.ReadValue)
            && request is ReadOrderRequest { Page: < 0 })
        {
            return ValueTask.FromResult<object?>(new InvalidPage("Page must be nonnegative"));
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

public union PlainDecision(OrderDto, Failure);
public partial class PlainOrderService : ApplicationService<IPlainOrderService>;

public sealed class ReadPlainOrder : PlainOrderService.Read
{
    public override Task<OrderDto> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new OrderDto(request.Id, "read", "local"));
}

public sealed class FindPlainOrder : PlainOrderService.Find
{
    public override Task<OrderDto?> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<OrderDto?>(null);
}

public sealed class DecidePlainOrder : PlainOrderService.Decide
{
    public override Task<PlainDecision> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(new Failure("order.missing", "Order not found"));
}

public sealed class DecideOrder : OrderServer.Decide
{
    public override Task<PlainDecision> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<PlainDecision>(new Failure("order.missing", "Order not found"));
}

public sealed class ReadProtectedOrderHandler : OrderServer.ReadProtected
{
    public override Task<PlainDecision> HandleAsync(
        ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("An anonymous request must not reach the protected handler");
}

public sealed class ReadOrderWithMessageHandler : OrderServer.ReadWithMessage
{
    public override Task<OrderReceipt> HandleAsync(ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<OrderReceipt>(new OrderReadReceipt(new OrderDto(request.Id, "cached", "read"), "Read from cache"));
}


[JsonSerializable(typeof(NOF.Sample.Application.NOFSampleService.DeleteConfigNode.Result), TypeInfoPropertyName = "DeleteConfigNodeResult")]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(SmokeIntent))]
[JsonSerializable(typeof(NOF.Sample.ConfigNodeName))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(UpdateBody))]
[JsonSerializable(typeof(UpdateOrderRequest))]
[JsonSerializable(typeof(ReadOrderRequest))]
[JsonSerializable(typeof(OrderReadReceipt))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
public partial class SmokeJsonContext : JsonSerializerContext;

public sealed record SmokeIntent(string Value);
public sealed class SmokeIntentHandler : IOutboxHandler<SmokeIntent>
{
    public static string? Delivered { get; private set; }
    public ValueTask HandleAsync(SmokeIntent message, OutboxDeliveryContext context, CancellationToken cancellationToken)
    {
        Delivered = message.Value;
        return ValueTask.CompletedTask;
    }
}

// This smoke exercises the codec and typed delivery delegate, without an AOT-incompatible memory ORM.
public sealed class SmokeOutboxStore : IOutboxStore
{
    public NOFOutboxMessage? Record { get; private set; }
    public void Stage(NOFOutboxMessage message) => Record = message;
    public async IAsyncEnumerable<NOFOutboxMessage> AtomicClaimPendingMessagesAsync(int batchSize = 100,
        TimeSpan? claimTimeout = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
    public ValueTask AtomicReleaseClaimsAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
    public ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(0);
    public ValueTask AtomicRecordDeliveryFailureAsync(NOFOutboxMessage message, string errorMessage, bool permanent = false,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => Task.FromResult(0);
}

public static class SmokeEndpointModule
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/native");
        group.Response<ValidateReadOrder.InvalidPage>(400);
        group.MapGet<OrderServer.ReadValue>("/orders/{id:guid}");
        group.MapPut<OrderServer.Update>("/orders/{id:guid}")
            .Response<Failure>(400);
        group.MapGet<OrderServer.ReadProtected>("/protected/{id:guid}");
        group.MapGet<PlainOrderService.Find>("/nullable/{id:guid}");
        group.MapGet<PlainOrderService.Decide>("/decisions/{id:guid}")
            .Response<Failure>(404);
        group.MapGet<OrderServer.ReadWithMessage>("/messages/{id:guid}");
        group.MapGet<ExpectedOrderService.Evaluate>("/expected/{id:guid}")
            .Response<ExpectedOrderService.Evaluate.Missing>(404)
.Response<ExpectedOrderService.Evaluate.Conflict>(409);
    }
}

public union OrderReceipt(OrderReadReceipt, Failure);

public sealed record OrderConflict(Guid Id);
public interface IExpectedOrderService : IApplicationService
{
    [FailureCase("Missing", "404")]
    [FailureCase("Conflict", "Conflict", typeof(OrderConflict))]
    OrderDto Evaluate(ReadOrderRequest request);
}
public partial class ExpectedOrderService : ApplicationService<IExpectedOrderService>;
public sealed class EvaluateOrder : ExpectedOrderService.Evaluate
{
    public override Task<Result> HandleAsync(
        ReadOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult<Result>(request.Page switch
        {
            < 0 => new Missing("Order not found"),
            0 => new Conflict(new OrderConflict(request.Id)),
            _ => new OrderDto(request.Id, "evaluated", "expected")
        });
}
