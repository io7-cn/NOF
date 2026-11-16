using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Notifications;
using NOF.Sample.Notifications.Persistence;

var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer((schema, context, ct) =>
    {
        if (context.JsonTypeInfo.Type == typeof(NOF.Sample.MessageReceiptKind))
        {
            schema.Enum = Enum.GetValues<NOF.Sample.MessageReceiptKind>()
                .Select(kind => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create((int)kind)!).ToList();
            schema.Description = "Known business projections: " + string.Join(", ",
                Enum.GetValues<NOF.Sample.MessageReceiptKind>().Select(kind => $"{(int)kind} = {kind}"))
                + ". Future consumers should display an unknown numeric kind without reinterpreting it.";
        }
        return Task.CompletedTask;
    });
});

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
app.MapGet<MessageDemoService.GetReceipts>("/api/messages/receipts").WithTags("Messages").WithSummary("Read persisted business projections, optionally by order ID");
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
