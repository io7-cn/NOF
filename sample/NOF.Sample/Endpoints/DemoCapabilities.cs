using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Services;
using System.Text;

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
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }));
        var group = app.MapGroup("/api/demo").WithTags("Local capability workbench");
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try
                {
                    await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem("Refresh the page and try again.", statusCode: 400);
                }
            }
            return await next(context);
        });
        group.MapGet("/outbox", async (ConfigurationDbContext db, DemoDeliveryControl control, Guid? orderId, CancellationToken ct) =>
        {
            var records = await db.Set<NOFOutboxMessage>().AsNoTracking().OrderByDescending(row => row.CreatedAtUtc).Take(100).ToListAsync(ct);
            var rows = records.Select(row => new
            {
                row.Id,
                row.MessageType,
                status = row.Status.ToString(),
                row.RetryCount,
                row.ErrorMessage,
                row.CreatedAtUtc,
                row.NextAttemptAtUtc,
                orderId = ReadOrderId(row.Payload),
                headers = ReadHeaders(row.Headers)
            }).Where(row => orderId is null || row.orderId == orderId);
            return Results.Ok(new { paused = control.Paused, items = rows });
        });
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
                    return Results.BadRequest();
            }
            return Results.Ok(new { paused = control.Paused, action });
        });
        group.MapGet("/receipts", async (Guid? orderId, IHttpClientFactory clients, CancellationToken ct) =>
        {
            using var response = await clients.CreateClient("notifications").GetAsync("/api/messages/receipts" + (orderId is null ? "" : $"?orderId={orderId}"), ct);
            return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
        });
        group.MapPost("/redeliver", async (DemoInput input, RabbitMQMessageClient client, CancellationToken ct) =>
        {
            if (input.OrderId is not { } id)
            {
                return Results.BadRequest("Select an order first.");
            }
            var headers = new Dictionary<string, string?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString() };
            await client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, new OrderCreated(id), headers, ct);
            headers[NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString();
            await client.PublishAsync("", RabbitMQDemoRoutes.MailQueue, new SendEmail(input.Email, "Repeated delivery", "Business idempotency", id), headers, ct);
            return Results.Ok(new { orderId = id, message = "Same business order, new delivery IDs. Projection count should remain three." });
        });
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
                return Results.BadRequest();
            }
            var value = await cache.GetAsync<string>(Key, ct);
            return Results.Ok(new { tenant = input.Tenant, present = value.HasValue, value = value.HasValue ? value.Value : null });
        });
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
                return Results.BadRequest();
            }
            var stored = await storage.OpenReadAsync(Bucket, Key, ct);
            if (!stored.HasValue)
            {
                return Results.Ok(new { exists = false, value = (string?)null });
            }
            await using var content = stored.Value.Content;
            using var reader = new StreamReader(content);
            return Results.Ok(new { exists = true, value = await reader.ReadToEndAsync(ct) });
        });
        group.MapPost("/events", async (DemoInput input, IEventPublisher publisher, DemoObservations observations, CancellationToken ct) =>
        {
            await publisher.PublishAsync(new DemoEvent(input.Value, DateTimeOffset.UtcNow), Context.Empty, ct);
            return Results.Ok(new { items = observations.Events.ToArray() });
        });
        group.MapPost("/ids", (IIdGenerator ids) => Results.Ok(new { ids = Enumerable.Range(0, 5).Select(_ => ids.NextId().ToString()).ToArray() }));
        group.MapPost("/value-object", (DemoInput input) => ConfigNodeName.TryFrom(input.Value, out var name, out var error)
            ? Results.Ok(new { valid = true, value = name.ToString() })
            : Results.BadRequest(new { valid = false, error }));
        group.MapGet("/invocations", (DemoObservations observations) => Results.Ok(new { items = observations.Invocations.Reverse().ToArray() }));
    }

    private static System.Text.Json.JsonElement ReadHeaders(string headers)
    {
        using var json = System.Text.Json.JsonDocument.Parse(headers);
        return json.RootElement.Clone();
    }

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
