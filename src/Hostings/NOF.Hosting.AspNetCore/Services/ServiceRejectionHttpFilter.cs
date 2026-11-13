using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>Preserves the existing native authorization presentation using returned values.</summary>
public sealed class ServiceRejectionHttpFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        return result is AuthorizationInvocationFilter.AuthenticationRequired or AuthorizationInvocationFilter.PermissionDenied or AuthorizationInvocationFilter.PolicyDenied ? ToProblem(result) : result;
    }
    /// <summary>Maps built-in policy values to the existing ProblemDetails shape, without exception propagation.</summary>
    public static ProblemHttpResult ToProblem(object failure)
    {
        var (status, code, message) = failure switch
        {
            AuthorizationInvocationFilter.AuthenticationRequired value => (401, value.ErrorCode, value.Message),
            AuthorizationInvocationFilter.PermissionDenied value => (403, value.Error.ErrorCode, value.Error.Message),
            AuthorizationInvocationFilter.PolicyDenied value => (400, value.Error.ErrorCode, value.Error.Message),
            _ => throw new InvalidOperationException($"No native HTTP mapping is configured for filter failure '{failure.GetType()}'.")
        };
        return TypedResults.Problem(statusCode: status, title: "Application request rejected", detail: message,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
