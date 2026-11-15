using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var rabbitMqUserName = builder.AddParameter("rabbitMqUserName", "guest");
var rabbitMqPassword = builder.AddParameter("rabbitMqPassword", "guest");
var rabbitMq = builder.AddRabbitMQ("rabbitmq", rabbitMqUserName, rabbitMqPassword)
    .WithManagementPlugin(15672)
    .WithLifetime(ContainerLifetime.Persistent);
rabbitMqUserName.WithParentRelationship(rabbitMq);
rabbitMqPassword.WithParentRelationship(rabbitMq);

var garnetPassword = builder.AddParameter("garnetPassword", "123456");
var garnet = builder.AddGarnet("garnet", password: garnetPassword)
    .WithLifetime(ContainerLifetime.Persistent);
garnetPassword.WithParentRelationship(garnet);

var postgresUserName = builder.AddParameter("postgresUserName", "postgres");
var postgresPassword = builder.AddParameter("postgresPassword", "123456");
var postgres = builder.AddPostgres("postgres", postgresUserName, postgresPassword)
    .WithPgAdmin(pgAdmin => pgAdmin.WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);
postgresUserName.WithParentRelationship(postgres);
postgresPassword.WithParentRelationship(postgres);
var database = postgres.AddDatabase("db2");

var keycloakAdmin = builder.AddParameter("keycloakAdmin", "nof-admin");
var keycloakPassword = builder.AddParameter("keycloakPassword", "nof-admin-local");
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.8.1")
    .WithHttpEndpoint(port: 18080, targetPort: 8080, name: "http")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", keycloakAdmin)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakPassword)
    .WithBindMount("./Realms", "/opt/keycloak/data/import", isReadOnly: true)
    .WithArgs("start-dev", "--import-realm")
    .WithHttpHealthCheck("/realms/nof/.well-known/openid-configuration");

var notifications = builder.AddProject<NOF_Sample_Notifications>("sample-notifications")
    .WithReference(rabbitMq, "rabbitmq")
    .WaitFor(rabbitMq)
    .WithHttpHealthCheck("/health");

var sample = builder.AddProject<NOF_Sample>("NOF-Sample")
    .WithReference(rabbitMq, "rabbitmq")
    .WaitFor(rabbitMq)
    .WithReference(database, "postgres")
    .WaitFor(database)
    .WithReference(garnet, "redis")
    .WaitFor(garnet)
    // Establish durable topic subscriptions before publishing demonstration events.
    .WaitFor(notifications)
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/nof"))
    .WithEnvironment("Authentication__ClientId", "nof-sample-web")
    .WithEnvironment("Authentication__ClientSecret", "nof-local-client-secret")
    .WithEnvironment("Notifications__BaseUrl", notifications.GetEndpoint("http"));

builder.Build().Run();
