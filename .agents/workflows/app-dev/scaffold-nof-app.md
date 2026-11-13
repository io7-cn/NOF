---
description: Scaffold a minimal NOF ASP.NET Core application with clean layers, generated RPC, and the EF Core provider
---

# Scaffold a New NOF Web Application

## Project Structure

```text
MyApp/
  MyApp/                    host, Program.cs, AppDbContext, appsettings.json
  MyApp.Domain/             domain classes, value objects, failures, events
  MyApp.Application/        application services and handlers, mappings, cache keys
  MyApp.Contract/           application service contracts, messages, DTOs
```

## 1. Create the Solution and Projects

```bash
mkdir MyApp
cd MyApp
dotnet new sln -n MyApp
dotnet new web -n MyApp -o MyApp
dotnet new classlib -n MyApp.Domain -o MyApp.Domain
dotnet new classlib -n MyApp.Application -o MyApp.Application
dotnet new classlib -n MyApp.Contract -o MyApp.Contract
dotnet sln add MyApp/MyApp.csproj MyApp.Domain/MyApp.Domain.csproj MyApp.Application/MyApp.Application.csproj MyApp.Contract/MyApp.Contract.csproj
```

## 2. Add Packages

```bash
dotnet add MyApp/MyApp.csproj package NOF.Hosting.AspNetCore
dotnet add MyApp/MyApp.csproj package NOF.Infrastructure.EntityFrameworkCore
dotnet add MyApp/MyApp.csproj package Npgsql.EntityFrameworkCore.PostgreSQL

dotnet add MyApp.Domain/MyApp.Domain.csproj package NOF.Domain
dotnet add MyApp.Contract/MyApp.Contract.csproj package NOF.Contract
dotnet add MyApp.Application/MyApp.Application.csproj package NOF.Application
```

The runtime packages carry their layer-specific analyzers/source generators in the NuGet package.

## 3. Add Project References

```bash
dotnet add MyApp.Application/MyApp.Application.csproj reference MyApp.Domain/MyApp.Domain.csproj MyApp.Contract/MyApp.Contract.csproj
dotnet add MyApp/MyApp.csproj reference MyApp.Application/MyApp.Application.csproj
```

## 4. Define a Contract

Create `MyApp.Contract/IAppService.cs`:

```csharp
using NOF.Contract;

namespace MyApp.Contract;

public interface IAppService : IApplicationService
{
    HealthResponse Health(Empty request);
}

public sealed record HealthResponse(string Status);
```

## 5. Implement the RPC Server

Create `MyApp.Application/AppService.cs`:

```csharp
using NOF.Application;
using NOF.Contract;
using MyApp.Contract;

namespace MyApp.Application;

public partial class AppService : ApplicationService<IAppService>;

public sealed class Health : AppService.Health
{
    public override Task<HealthResponse> HandleAsync(
        Empty request,
        Context context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HealthResponse("ok"));
    }
}
```

## 6. Add the Host DbContext

Create `MyApp/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

namespace MyApp;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : NOFDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
```

## 7. Configure Program.cs

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp;
using MyApp.Application;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.AddApplicationPart(typeof(AppService).Assembly);
builder.AddApplicationService<AppService>();

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();

var app = builder.Build();
app.UseNOF();
app.MapOrderEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

## 8. Map native HTTP endpoints

Place an ordinary feature extension in the host project and call it after Build:

```csharp
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");
        group.MapGet<AppService.Health>("/health");
        return group;
    }
}
```

Keep HTTP namespaces and module types in the host project; the Contract project references no Hosting package.

## 9. Configure appsettings.json

```json
{
  "ConnectionStrings": {
    "postgres": "Host=localhost;Database=myapp;Username=postgres;Password=postgres"
  }
}
```

## Notes

- `WebApplication.CreateBuilder(args).AddNOF()` registers NOF infrastructure, JSON/OpenAPI services, health endpoints, and in-memory defaults.
- `AddApplicationPart(...)` executes generated assembly initializers; `AddApplicationService<T>()` explicitly registers the RPC server.
- Call app.UseNOF() and ordinary feature extensions after native Build(); map native health checks/OIDC explicitly.
- Add RabbitMQ, Redis, or OIDC packages only when the application needs those providers.
- Application handlers should inject `IDbContext` / `IRepository<T>`; keep EF Core types in the host.
