using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore;

/// <summary>
/// Binds and restores scoped convenience services for every ASP.NET Core request so ambient infrastructure
/// such as mapper, event publisher, and context accessors are available consistently.
/// </summary>
public sealed class NOFAmbientBindingMiddleware
{
    private readonly RequestDelegate _next;

    public NOFAmbientBindingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var binding = context.RequestServices.BindNOF(Context.Current);
        await _next(context);
    }
}
