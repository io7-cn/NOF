using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Persistence;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class NotificationIdempotencyTests
{
    [Fact]
    public async Task RepeatedDeliveries_UseBusinessUniqueness_WithoutFrameworkReceipts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddApplicationPart(typeof(MessageDemoService).Assembly);
        builder.AddApplicationService<MessageDemoService>();
        builder.Services.AddScoped<INotificationReceipts, NotificationReceipts>();
        var connection = $"Data Source=business-idempotency-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        builder.UseDbContext<NotificationsDbContext>((_, ef) => ef.UseSqlite(connection))
            .ConfigureNOFDbContextOptions(options => options.TenantMode = TenantMode.SharedDatabase);
        builder.Services.AddMemoryMessaging(endpoints =>
        {
            endpoints.MapQueue<MessageDemoService.SendMail>("mail");
            endpoints.MapSubscription<MessageDemoService.RecordOrder>("orders", "audit");
            endpoints.MapSubscription<CountOrder>("orders", "metrics");
        });
        using var host = builder.Build();
        var orderId = Guid.NewGuid();
        // Different delivery IDs still describe the same business operation.
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = host.Services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>();
            await client.SendAsync("mail", new SendEmail("demo@example.test", "Subject", "Body", orderId), Context.Empty);
            await client.PublishAsync("orders", new OrderCreated(orderId), Context.Empty);
        }));
        using var verification = host.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var rows = await db.Receipts.ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(row => row.Kind).Distinct().Count());
        Assert.All(rows, row => Assert.Equal($"order:{orderId}", row.IdempotencyKey));
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.Name.Contains("Inbox"));
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.Name.Contains("Outbox"));
        Assert.Null(verification.ServiceProvider.GetService<IOutbox>());

        // The same key belongs to another tenant independently, including the native SQL path.
        using (var other = host.Services.CreateScope())
        {
            var client = other.ServiceProvider.GetRequiredService<MemoryMessageClient>();
            await client.SendAsync("mail", new SendEmail("other@example.test", "Subject", "Body", orderId),
                Context.Empty.WithTenantId("other"));
        }
        using var tenantContext = Context.PushCurrent(Context.Empty.WithTenantId("other"));
        using var tenantScope = host.Services.CreateScope();
        var tenantDb = tenantScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Single(await tenantDb.Receipts.ToListAsync());
        Assert.Equal(4, await tenantDb.Receipts.IgnoreQueryFilters().CountAsync());
    }
}
