using Microsoft.AspNetCore.Http;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>Maps application policy rejection to native ProblemDetails without changing business responses.</summary>
public sealed class ServiceRejectionHttpFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ServiceRejectedException rejected)
        {
            var status = context.HttpContext.Items[NOFInfrastructureConstants.Transport.Headers.HttpStatusCode] is int supplied
                ? supplied
                : int.TryParse(rejected.Failure.Code, out var parsed) && parsed is >= 400 and <= 599 ? parsed : 500;
            return TypedResults.Problem(statusCode: status, title: "Application request rejected",
                detail: rejected.Failure.Message,
                extensions: new Dictionary<string, object?> { ["code"] = rejected.Failure.Code });
        }
    }
}
