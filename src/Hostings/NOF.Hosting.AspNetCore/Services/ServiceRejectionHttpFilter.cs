using Microsoft.AspNetCore.Http;
using NOF.Application;
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
            var status = rejected.Reason switch
            {
                RejectionReason.Authentication => StatusCodes.Status401Unauthorized,
                RejectionReason.Authorization => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return TypedResults.Problem(statusCode: status, title: "Application request rejected",
                detail: rejected.Failure.Message,
                extensions: new Dictionary<string, object?> { ["code"] = rejected.Failure.Code });
        }
    }
}
