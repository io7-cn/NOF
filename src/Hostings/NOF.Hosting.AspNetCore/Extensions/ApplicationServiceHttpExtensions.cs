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
    /// <remarks>Policy rejection throws ServiceRejectedException; HTTP adapters choose its response representation.</remarks>
    public static async Task<TResponse> InvokeAsync<TRequest, TResponse>(this HttpContext http,
        ServiceOperationDescriptor<TRequest, TResponse> operation, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(operation);
        var execution = await http.RequestServices.GetRequiredService<HttpRequestInboundAdapter>()
            .InvokeAsync(operation, request!, http, cancellationToken).ConfigureAwait(false);
        return execution.Execution.Require<TResponse>(allowNull: operation.AllowsNullResponse);
    }
}
