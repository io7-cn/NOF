using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Endpoints.Models;
using NOF.Sample.Services;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace NOF.Sample.Endpoints;

public static class DemoCapabilities
{
    public static void MapDemoCapabilities(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Demo").LogInformation("Local capability workbench: /demo.html");
        app.MapGet("/api/demo/security-token", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new SecurityTokenResponse(antiforgery.GetAndStoreTokens(http).RequestToken)))
            .WithName("GetDemoSecurityToken").WithSummary("Issue a local workbench antiforgery token")
            .Produces<SecurityTokenResponse>().WithTags("Local capability workbench");
        var group = app.MapGroup("/api/demo").WithTags("Local capability workbench")
            .WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                if (context.HttpContext.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: true })
                {
                    return Results.Problem("Refresh the page and try again.", statusCode: 400);
                }
            }
            return await next(context);
        });
        group.MapGet("/outbox", async (ConfigurationDbContext db, DemoDeliveryControl control, Guid? orderId, CancellationToken ct) =>
        {
            var records = await db.Set<NOFOutboxMessage>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtc).Take(100).ToListAsync(ct);
            var rows = records.Select(row => new DemoOutboxEntry(row.Id, row.MessageType, row.Status, row.RetryCount,
                row.ErrorMessage, row.CreatedAtUtc, row.NextAttemptAtUtc, ReadOrderId(row.Payload), ReadHeaders(row.Headers)))
                .Where(row => orderId is null || row.OrderId == orderId).ToArray();
            return Results.Ok(new DemoOutboxOverview(control.Paused, rows));
        }).WithName("GetDemoOutbox").WithSummary("Inspect the latest 100 transactional Outbox intents")
            .WithDescription("Optionally filter that recent window by business order ID. This is a development-only inspection view.")
            .Produces<DemoOutboxOverview>().ProducesProblem(400);
        group.MapPost("/delivery/{action}", (string action, DemoDeliveryControl control) =>
        {
            switch (action)
            {
                case "pause":
                    control.Pause();
                    break;
                case "resume":
                    control.Resume();
                    break;
                case "fail-next":
                    control.FailNext();
                    break;
                default:
                    return Results.Problem("Unsupported action.", statusCode: 400);
            }
            return Results.Ok(new DemoDeliveryResponse(control.Paused, action));
        }).WithName("ControlDemoDelivery").WithSummary("Pause, resume or fail the next outgoing delivery")
            .Produces<DemoDeliveryResponse>().ProducesProblem(400)
            .AddOpenApiOperationTransformer((operation, _, _) => DescribeActions(operation, ["pause", "resume", "fail-next"]));
        group.MapGet("/receipts", async (Guid? orderId, IHttpClientFactory clients,
            Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken ct) =>
        {
            try
            {
                using var response = await clients.CreateClient("notifications").GetAsync("/api/messages/receipts" + (orderId is null ? "" : $"?orderId={orderId}"), ct);
                if (response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<MessageReceiptsResponse>(json.Value.SerializerOptions, ct) is { } receipts)
                {
                    return Results.Ok(receipts);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
            {
                // The gateway owns this HTTP contract; it does not expose arbitrary downstream failures.
            }
            return Results.Problem("The notification service did not return a valid receipt response.", statusCode: 502);
        }).WithName("GetDemoReceipts").WithSummary("Read business projections from the notification service")
            .Produces<MessageReceiptsResponse>().ProducesProblem(400).ProducesProblem(502);
        group.MapPost("/redeliver", async (DemoInput input, RabbitMQMessageClient client, CancellationToken ct) =>
        {
            if (input.OrderId is not { } id)
            {
                return Results.Problem("Select an order first.", statusCode: 400);
            }
            var headers = new Dictionary<string, string?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString() };
            await client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, new OrderCreated(id), headers, ct);
            headers[NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString();
            await client.PublishAsync("", RabbitMQDemoRoutes.MailQueue, new SendEmail(input.Email, "Repeated delivery", "Business idempotency", id), headers, ct);
            return Results.Ok(new DemoRedeliveryResponse(id, "Same business order, new delivery IDs. Projection count should remain three."));
        }).WithName("RedeliverDemoOrder").WithSummary("Replay an order with fresh transport message IDs")
            .Produces<DemoRedeliveryResponse>().ProducesProblem(400).ProducesProblem(415);
        group.MapPost("/cache/{action}", async (string action, DemoInput input, ICacheService cache, CancellationToken ct) =>
        {
            using var context = Context.PushCurrent(Context.Empty.WithTenantId(input.Tenant));
            const string Key = "capabilities:value";
            if (action == "write")
            {
                await cache.SetAsync(Key, input.Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
            }
            else if (action == "delete")
            {
                await cache.RemoveAsync(Key, ct);
            }
            else if (action != "read")
            {
                return Results.Problem("Unsupported action.", statusCode: 400);
            }
            var value = await cache.GetAsync<string>(Key, ct);
            return Results.Ok(new DemoCacheResponse(input.Tenant, value.HasValue, value.HasValue ? value.Value : null));
        }).WithName("UseDemoCache").WithSummary("Read, write or delete a tenant-scoped cache entry")
            .Produces<DemoCacheResponse>().ProducesProblem(400).ProducesProblem(415)
            .AddOpenApiOperationTransformer((operation, _, _) => DescribeActions(operation, ["read", "write", "delete"]));
        group.MapPost("/storage/{action}", async (string action, DemoInput input, IObjectStorage storage, CancellationToken ct) =>
        {
            const string Bucket = "capabilities";
            const string Key = "demo.txt";
            if (action == "write")
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.Value));
                await storage.PutAsync(Bucket, Key, stream, cancellationToken: ct);
            }
            else if (action == "delete")
            {
                await storage.DeleteAsync(Bucket, Key, ct);
            }
            else if (action != "read")
            {
                return Results.Problem("Unsupported action.", statusCode: 400);
            }
            var stored = await storage.OpenReadAsync(Bucket, Key, ct);
            if (!stored.HasValue)
            {
                return Results.Ok(new DemoStorageResponse(false, null));
            }
            await using var content = stored.Value.Content;
            using var reader = new StreamReader(content);
            return Results.Ok(new DemoStorageResponse(true, await reader.ReadToEndAsync(ct)));
        }).WithName("UseDemoStorage").WithSummary("Read, write or delete the demonstration object")
            .Produces<DemoStorageResponse>().ProducesProblem(400).ProducesProblem(415)
            .AddOpenApiOperationTransformer((operation, _, _) => DescribeActions(operation, ["read", "write", "delete"]));
        group.MapPost("/events", async (DemoInput input, IEventPublisher publisher, DemoObservations observations, CancellationToken ct) =>
        {
            await publisher.PublishAsync(new DemoEvent(input.Value, DateTimeOffset.UtcNow), Context.Empty, ct);
            return Results.Ok(new DemoEventsResponse(observations.Events.ToArray()));
        }).WithName("PublishDemoEvent").WithSummary("Publish a local event to discovered handlers")
            .Produces<DemoEventsResponse>().ProducesProblem(400).ProducesProblem(415);
        group.MapPost("/ids", (IIdGenerator ids) => Results.Ok(new DemoIdsResponse(Enumerable.Range(0, 5).Select(_ => ids.NextId().ToString()).ToArray())))
            .WithName("GenerateDemoIds").WithSummary("Generate five Snowflake IDs as strings")
            .Produces<DemoIdsResponse>().ProducesProblem(400);
        group.MapPost("/value-object", (DemoInput input) => ConfigNodeName.TryFrom(input.Value, out var name, out var error)
            ? Results.Ok(new DemoValueResponse(true, name.ToString()))
            : Results.BadRequest(new DemoValueResponse(false, Error: error)))
            .WithName("ValidateDemoValue").WithSummary("Try to construct a configuration node name")
            .Produces<DemoValueResponse>().Produces<DemoValueResponse>(400).ProducesProblem(400).ProducesProblem(415);
        group.MapGet("/invocations", (DemoObservations observations) => Results.Ok(new DemoInvocationsResponse(observations.Invocations.Reverse().ToArray())))
            .WithName("GetDemoInvocations").WithSummary("Inspect the latest application invocation observations")
            .Produces<DemoInvocationsResponse>();
    }

    private static Task DescribeActions(OpenApiOperation operation, IReadOnlyList<string> choices)
    {
        if (operation.Parameters?.FirstOrDefault(item => item.Name == "action") is OpenApiParameter action)
        {
            action.Description = "Supported actions; other values return 400.";
            action.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = choices.Select(value => (JsonNode)JsonValue.Create(value)!).ToList() };
        }
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, string?> ReadHeaders(string headers)
        => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(headers)
            ?? throw new System.Text.Json.JsonException("Outbox headers must be a JSON object.");

    private static Guid? ReadOrderId(byte[] payload)
    {
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("orderId", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetGuid(out var id))
            {
                return id;
            }
        }
        return null;
    }
}
