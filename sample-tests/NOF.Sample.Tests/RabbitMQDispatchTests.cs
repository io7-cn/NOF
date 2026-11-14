using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Application;
using NOF.Sample.Endpoints;
using NOF.Sample.Messaging;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Persistence;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using ConsumerService = NOF.Sample.Notifications.Application.MessageDemoService;
using Ef = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NOF.Sample.Tests;

public sealed class RabbitMQDispatchTests
{
    [Fact]
    public async Task InvalidRecipient_DoesNotStageOrDispatchMessages()
    {
        await using var producer = CreateProducer("Host=127.0.0.1;Port=1;UserName=unused;Password=unused", MemoryDatabase());
        await producer.StartAsync();
        using var client = producer.GetTestClient();
        using var response = await client.PostAsJsonAsync("/api/messages/orders", new SubmitDemoOrderRequest("not-an-email"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = producer.Services.CreateScope();
        Assert.Equal(0, await Ef.CountAsync(scope.ServiceProvider.GetRequiredService<ProducerDbContext>().Set<NOFOutboxMessage>()));
        Assert.Null(scope.ServiceProvider.GetService<IInboxStore>());
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
        await AssertInboxAsync(consumer, 3, 2);
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
        await AssertInboxAsync(restarted, 6, 4);
        using var producerScope = producer.Services.CreateScope();
        using var receiverScope = restarted.Services.CreateScope();
        var ids = await Ef.ToListAsync(producerScope.ServiceProvider.GetRequiredService<ProducerDbContext>().Set<NOFOutboxMessage>().Select(item => item.Id));
        var receivedIds = await Ef.ToListAsync(receiverScope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Set<NOFInboxMessage>().Select(item => item.Id).Distinct());
        Assert.Equal(ids.Order(), receivedIds.Order()); // The outbox identity is preserved, including fanout copies.
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production }).AddNOF();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.Configure<TransactionalMessageOptions>(options =>
        {
            options.Inbox.PollingInterval = options.Outbox.PollingInterval = TimeSpan.FromMilliseconds(100);
            options.Outbox.RetryDelay = TimeSpan.FromMilliseconds(100);
        });
        return builder;
    }

    private static WebApplication CreateProducer(string connection, string database)
    {
        var builder = Builder();
        builder.AddApplicationPart(typeof(MessageDispatchService).Assembly);
        builder.AddApplicationService<MessageDispatchService>();
        builder.Services.AddOutbox(outbox =>
        {
            outbox.Add<SendEmail, SendEmailOutboxHandler>("sample.send-email", 1);
            outbox.Add<OrderCreated, OrderCreatedOutboxHandler>("sample.order-created", 1);
        });
        builder.Services.AddRabbitMQ(options => options.ConnectionString = connection);
        builder.UseDbContext<ProducerDbContext>().WithTenantMode(TenantMode.SharedDatabase).WithConnectionString(database).WithOptions((options, value) => options.UseSqlite(value));
        var app = builder.Build();
        app.UseNOF();
        app.MapMessageDispatchEndpoints();
        return app;
    }

    private static WebApplication CreateConsumer(string connection, string database)
    {
        var builder = Builder();
        builder.AddApplicationPart(typeof(ConsumerService).Assembly);
        builder.AddApplicationService<ConsumerService>();
        builder.Services.AddScoped<NOF.Sample.Notifications.Application.INotificationReceipts, NotificationReceipts>();
        builder.Services.AddRabbitMQ(options => options.ConnectionString = connection, NotificationMessaging.Configure);
        builder.UseDbContext<NotificationsDbContext>().WithTenantMode(TenantMode.SharedDatabase).WithConnectionString(database).WithOptions((options, value) => options.UseSqlite(value));
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

    private static async Task AssertInboxAsync(WebApplication app, int expectedCount, int expectedIds)
    {
        using var scope = app.Services.CreateScope();
        var records = await Ef.ToListAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Set<NOFInboxMessage>().AsNoTracking());
        Assert.Equal(expectedCount, records.Count);
        Assert.Equal(expectedIds, records.Select(item => item.Id).Distinct().Count());
        Assert.All(records, record => Assert.Equal(InboxMessageStatus.Processed, record.Status));
        Assert.Contains(records, record => record.Route == "rabbitmq:queue:" + RabbitMQDemoRoutes.MailQueue);
        Assert.Contains(records, record => record.Route == "rabbitmq:subscription:" + RabbitMQDemoRoutes.AuditSubscription);
        Assert.Contains(records, record => record.Route == "rabbitmq:subscription:" + RabbitMQDemoRoutes.MetricsSubscription);
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
