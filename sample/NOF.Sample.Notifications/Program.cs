using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Persistence;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.AddApplicationPart(typeof(MessageDemoService).Assembly);
builder.AddApplicationService<MessageDemoService>();

builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.")))
    .ConfigureRabbitMQ(NotificationMessaging.Configure);

builder.UseDbContext<NotificationsDbContext>((_, ef) => ef.UseSqlite(
        builder.Configuration.GetConnectionString("notifications") ?? "Data Source=notifications.db"))
    .ConfigureNOFDbContextOptions(options => options.TenantMode = TenantMode.SharedDatabase);

builder.Services.AddScoped<INotificationReceipts, NotificationReceipts>();

var app = builder.Build();
app.UseNOF();
app.MapGet<MessageDemoService.GetReceipts>("/api/messages/receipts");
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
