using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Application;
using NOF.Sample.Notifications.Persistence;

var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.AddApplicationPart(typeof(MessageDemoService).Assembly);
builder.AddApplicationService<MessageDemoService>();
builder.Services.AddScoped<INotificationReceipts, NotificationReceipts>();
builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(500));
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.")));
builder.Services.ConfigureRabbitMQ(NotificationMessaging.Configure);
// The NOF SQLite factory initializes the fresh demo schema on first use. It persists receipts across restarts.
builder.UseDbContext<NotificationsDbContext>().WithTenantMode(TenantMode.SharedDatabase)
    .WithConnectionString(builder.Configuration.GetConnectionString("notifications") ?? "Data Source=notifications.db")
    .WithOptions((options, connection) => options.UseSqlite(connection));

var app = builder.Build();
app.UseNOF();
app.MapGet<MessageDemoService.GetReceipts>("/api/messages/receipts");
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
