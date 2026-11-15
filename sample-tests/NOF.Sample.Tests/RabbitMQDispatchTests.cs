using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Application;
using NOF.Sample.Endpoints;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Persistence;
using NOF.Sample.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using ConsumerService = NOF.Sample.Notifications.MessageDemoService;
using Ef = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NOF.Sample.Tests;

public sealed class RabbitMQDispatchTests
{
    [Fact]
    public async Task InvalidRecipient_DoesNotStageOrDispatchMessages()
    {
        await using var producer = CreateProducer("amqp://unused:unused@127.0.0.1:1/", MemoryDatabase());
        await producer.StartAsync();
        using var client = producer.GetTestClient();
        using var response = await client.PostAsJsonAsync("/api/messages/orders", new SubmitDemoOrderRequest("not-an-email"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = producer.Services.CreateScope();
        Assert.Equal(0, await Ef.CountAsync(scope.ServiceProvider.GetRequiredService<ProducerDbContext>().Set<NOFOutboxMessage>()));
        Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<ProducerDbContext>().Model.GetEntityTypes(), type => type.Name.Contains("Inbox"));
    }

    [RabbitMQSampleFact]
    public async Task ExplicitHeaders_SurviveOutboxAndNativeRabbitMQ_WithoutOrderingInterpretation()
    {
        var connection = Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")!;
        await using var consumer = CreateConsumer(connection, MemoryDatabase());
        await consumer.StartAsync();
        await using var producer = CreateProducer(connection, MemoryDatabase());
        var headers = new Dictionary<string, string?>
        {
            ["correlation-id"] = "original",
            ["optional"] = null,
            ["X-Order-Key"] = "opaque",
            ["X-Sequence"] = "not-a-number"
        };
        Guid id;
        using (var scope = producer.Services.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
                new SendEmail("headers@example.test", "Headers", "Snapshot"), Context.Empty, headers);
            headers["correlation-id"] = "changed";
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        await producer.StartAsync();
        var deliveries = consumer.Services.GetRequiredService<DeliveryProbe>();
        await WaitUntilAsync(() => Task.FromResult(deliveries.Items.Any(item => item[NOFAbstractionConstants.Transport.Headers.MessageId]?.GetValue<string>() == id.ToString())));
        var delivered = deliveries.Items.First(item => item[NOFAbstractionConstants.Transport.Headers.MessageId]?.GetValue<string>() == id.ToString());
        var received = delivered.ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<string>());
        Assert.Equal("original", received["correlation-id"]);
        Assert.Null(received["optional"]);
        Assert.Equal("opaque", received["X-Order-Key"]);
        Assert.Equal("not-a-number", received["X-Sequence"]);
        Assert.Equal(id.ToString(), received[NOFAbstractionConstants.Transport.Headers.MessageId]);
        Assert.Equal("host", received[NOFAbstractionConstants.Transport.Headers.TenantId]);
    }

    [RabbitMQSampleFact]
    public async Task TwoHosts_UseRabbitMQQueueAndFanout_AndRetainMessagesAcrossConsumerRestart()
    {
        var connection = Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")!;
        var producerDatabase = MemoryDatabase();
        var consumerDatabase = MemoryDatabase();
        await using var consumer = CreateConsumer(connection, consumerDatabase);
        await consumer.StartAsync();
        await using var producer = CreateProducer(connection, producerDatabase);
        await producer.StartAsync();
        using var producerClient = producer.GetTestClient();
        using var consumerClient = consumer.GetTestClient();
        var first = await SubmitAsync(producerClient, "first@example.test");
        var firstReceipts = await WaitForReceiptsAsync(consumerClient, first.OrderId);
        Assert.Equal(new[] { MessageReceiptKind.Mail, MessageReceiptKind.OrderAudit, MessageReceiptKind.OrderMetrics }, firstReceipts.Items.Select(item => item.Kind).Order().ToArray());
        Assert.Contains(firstReceipts.Items, item => item.Kind == MessageReceiptKind.Mail && item.Summary.Contains("first@example.test", StringComparison.Ordinal));
        await WaitUntilAsync(async () => await SentCountAsync(producer) == 2);

        // Durable broker queues outlive the receiver. Confirmed publishing can complete while it is offline.
        await consumer.StopAsync();
        var second = await SubmitAsync(producerClient, "offline@example.test");
        await WaitUntilAsync(async () => await SentCountAsync(producer) == 4);
        using (var scope = consumer.Services.CreateScope())
        {
            Assert.Equal(3, await Ef.CountAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Receipts));
            Assert.Null(scope.ServiceProvider.GetService<IOutbox>());
        }

        await using var restarted = CreateConsumer(connection, consumerDatabase);
        await restarted.StartAsync();
        using var restartedClient = restarted.GetTestClient();
        var secondReceipts = await WaitForReceiptsAsync(restartedClient, second.OrderId);
        Assert.Equal(3, secondReceipts.Items.Select(item => item.Kind).Distinct().Count());
        Assert.Equal(3, (await restartedClient.GetFromJsonAsync<MessageReceiptsResponse>($"/api/messages/receipts?orderId={first.OrderId}"))!.Items.Count);


    }

    [RabbitMQSampleFact]
    public async Task QueueOnlyAdapter_DoesNotCreateAMissingQueue()
    {
        var connection = Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")!;
        var queue = "sample.missing." + Guid.NewGuid().ToString("N");
        await using var consumer = CreateConsumer(connection, MemoryDatabase(), queue);
        var error = await Assert.ThrowsAsync<OperationInterruptedException>(() => consumer.StartAsync());
        Assert.Equal<ushort?>(404, error.ShutdownReason?.ReplyCode);
        var nativeConnection = await consumer.Services.GetRequiredService<RabbitMQConnection>().GetAsync();
        await using var channel = await nativeConnection.CreateChannelAsync();
        var missing = await Assert.ThrowsAsync<OperationInterruptedException>(() => channel.QueueDeclarePassiveAsync(queue));
        Assert.Equal<ushort?>(404, missing.ShutdownReason?.ReplyCode);
    }

    [RabbitMQSampleFact]
    public async Task NativeMandatoryPublish_RejectsUnroutableMessages()
    {
        var connection = Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")!;
        await using var producer = CreateProducer(connection, MemoryDatabase());
        await producer.StartAsync();
        using var scope = producer.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<RabbitMQMessageClient>();
        var headers = new Dictionary<string, string?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString() };
        var error = await Assert.ThrowsAsync<PublishException>(() => publisher.PublishAsync(string.Empty,
            "sample.unroutable." + Guid.NewGuid().ToString("N"), new SendEmail("demo@example.test", "Test", "Test"), headers, default).AsTask());
        Assert.True(error.IsReturn);
    }

    [RabbitMQSampleFact]
    public async Task NativeAndLegacyMessageIdentities_ArePreservedByTheActualConsumer()
    {
        var connection = Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")!;
        await using var consumer = CreateConsumer(connection, MemoryDatabase());
        await consumer.StartAsync();
        var orderId = Guid.NewGuid();
        var nativeId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        var ignoredHeaderId = Guid.NewGuid();
        var nativeConnection = await consumer.Services.GetRequiredService<RabbitMQConnection>().GetAsync();
        await using var channel = await nativeConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        using var scope = consumer.Services.CreateScope();
        var serializer = scope.ServiceProvider.GetRequiredService<IObjectSerializer>();
        var properties = new[]
        {
            new BasicProperties
            {
                MessageId = nativeId.ToString(),
                Headers = new Dictionary<string, object?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = ignoredHeaderId.ToString() }
            },
            new BasicProperties
            {
                Headers = new Dictionary<string, object?> { [NOFAbstractionConstants.Transport.Headers.MessageId.ToUpperInvariant()] = legacyId.ToString() }
            },
            new BasicProperties()
        };
        foreach (var property in properties)
        {
            await channel.BasicPublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, mandatory: true, property,
                serializer.Serialize(new SendEmail("demo@example.test", "Identity", "Test", orderId), typeof(SendEmail)));
        }
        using var client = consumer.GetTestClient();
        var deliveries = consumer.Services.GetRequiredService<DeliveryProbe>();
        await WaitUntilAsync(() => Task.FromResult(deliveries.Items.Count == 3));
        var ids = deliveries.Items.Select(item => item[NOFAbstractionConstants.Transport.Headers.MessageId]?.GetValue<string>()).ToArray();
        await WaitUntilAsync(async () => (await client.GetFromJsonAsync<MessageReceiptsResponse>($"/api/messages/receipts?orderId={orderId}"))!.Items.Count == 1);
        Assert.Contains(nativeId.ToString(), ids);
        Assert.Contains(legacyId.ToString(), ids);
        Assert.DoesNotContain(ignoredHeaderId.ToString(), ids);
        Assert.Contains(ids, id => id is null);
    }

    public sealed class DeliveryProbe
    {
        public System.Collections.Concurrent.ConcurrentBag<System.Text.Json.Nodes.JsonObject> Items { get; } = [];
    }
    public sealed class DeliveryCapture(DeliveryProbe probe) : IInvocationFilter
    {
        public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
        {
            var result = await next(context, input, ct);
            if (context.Origin == InvocationOrigin.Message)
            { probe.Items.Add(context.Items); }
            return result;
        }
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production }).AddNOF();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<OutboxOptions>(options =>
        {
            options.PollingInterval = TimeSpan.FromMilliseconds(100);
            options.RetryDelay = TimeSpan.FromMilliseconds(100);
        });
        builder.Services.AddSingleton<DeliveryProbe>();
        builder.Services.AddInvocationFilters(filters => filters.Global.Use<DeliveryCapture>());
        return builder;
    }

    private static WebApplication CreateProducer(string connection, string database)
    {
        var builder = Builder();
        builder.AddApplicationPart(typeof(MessageDispatchService).Assembly);
        builder.AddApplicationService<MessageDispatchService>();
        builder.AddApplicationPart(typeof(SendEmailOutboxHandler).Assembly);
        builder.Services.AddOutbox();
        builder.Services.AddSingleton<DemoDeliveryControl>();
        builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connection));
        builder.UseDbContext<ProducerDbContext>((_, ef) => ef.UseSqlite(database))
            .ConfigureNOFDbContextOptions(options => options.TenantMode = TenantMode.SharedDatabase);
        var app = builder.Build();
        app.UseNOF();
        app.MapMessageDispatchEndpoints();
        return app;
    }

    private static WebApplication CreateConsumer(string connection, string database, string? missingQueue = null)
    {
        var builder = Builder();
        builder.AddApplicationPart(typeof(ConsumerService).Assembly);
        builder.AddApplicationService<ConsumerService>();
        builder.Services.AddScoped<INotificationReceipts, NotificationReceipts>();
        if (missingQueue is null)
        {
            builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connection));
            var moduleQueue = "sample.module." + Guid.NewGuid().ToString("N");
            builder.Services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<ConsumerService.SendMail>(moduleQueue));
            builder.Services.ConfigureRabbitMQ(NotificationMessaging.Configure);
            builder.Services.ConfigureRabbitMQ(rabbit => rabbit.Topology(async (channel, ct) =>
                await channel.QueueDeclareAsync(moduleQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct)));
        }
        else
        {
            builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connection));
            builder.Services.ConfigureRabbitMQ(endpoints => endpoints.MapQueue<ConsumerService.SendMail>(missingQueue));
        }
        builder.UseDbContext<NotificationsDbContext>((_, ef) => ef.UseSqlite(database))
            .ConfigureNOFDbContextOptions(options => options.TenantMode = TenantMode.SharedDatabase);
        var app = builder.Build();
        app.UseNOF();
        app.MapGet<ConsumerService.GetReceipts>("/api/messages/receipts");
        return app;
    }

    private static async Task<SubmittedDemoOrder> SubmitAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/messages/orders", new SubmitDemoOrderRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmittedDemoOrder>())!;
    }

    private static async Task<MessageReceiptsResponse> WaitForReceiptsAsync(HttpClient client, Guid orderId)
    {
        MessageReceiptsResponse? result = null;
        await WaitUntilAsync(async () =>
        {
            result = await client.GetFromJsonAsync<MessageReceiptsResponse>($"/api/messages/receipts?orderId={orderId}");
            return result?.Items.Count == 3;
        });
        return result!;
    }

    private static async Task<int> SentCountAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        return await Ef.CountAsync(scope.ServiceProvider.GetRequiredService<ProducerDbContext>().Set<NOFOutboxMessage>(), item => item.Status == OutboxMessageStatus.Sent);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            await Task.Delay(100, timeout.Token);
        }
    }

    private static string MemoryDatabase() => $"Data Source=rabbit-demo-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    public sealed class ProducerDbContext(DbContextOptions<ProducerDbContext> options) : NOFDbContext(options);
}

public sealed class RabbitMQSampleFactAttribute : FactAttribute
{
    public RabbitMQSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOF_SAMPLE_RABBITMQ")))
        {
            Skip = "Set NOF_SAMPLE_RABBITMQ to an isolated RabbitMQ connection string to run the live two-host demo.";
        }
    }
}
