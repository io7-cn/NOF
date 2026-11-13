using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>Bridges native HTTP endpoint binding into the application policy boundary.</summary>
public static class ApplicationServiceHttpExtensions
{
    /// <summary>Runs HTTP authentication and application policies, returning the declared business value.</summary>
    /// <remarks>The full pipeline is open; native endpoints must map declared filter failures alongside business values.</remarks>
    public static async Task<object?> InvokeAsync<TRequest, TResponse>(this HttpContext http,
        ServiceOperationDescriptor<TRequest, TResponse> operation, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(operation);
        var execution = await http.RequestServices.GetRequiredService<HttpRequestInboundAdapter>()
            .InvokeAsync(operation, request!, http, cancellationToken).ConfigureAwait(false);
        return execution;
    }
}
