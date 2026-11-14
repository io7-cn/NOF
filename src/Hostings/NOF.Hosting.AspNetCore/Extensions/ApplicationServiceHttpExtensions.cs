using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>Bridges native HTTP endpoint binding into the application policy boundary.</summary>
public static class ApplicationServiceHttpExtensions
{
    /// <summary>Runs native HTTP entry using the default semantic context.</summary>
    /// <remarks>Endpoints map business values and declared filter failures; built-in authorization failures are presented as problems.</remarks>
    public static Task<object?> InvokeAsync<TRequest, TResponse>(this HttpContext http,
        ServiceOperationDescriptor<TRequest, TResponse> operation, TRequest request, CancellationToken cancellationToken = default)
        => http.InvokeAsync(operation, request, Context.Empty, cancellationToken);

    /// <summary>Runs native HTTP entry with explicitly selected semantic context.</summary>
    public static async Task<object?> InvokeAsync<TRequest, TResponse>(this HttpContext http,
        ServiceOperationDescriptor<TRequest, TResponse> operation, TRequest request, Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(operation);
        http.BindIdentity();
        var result = await http.RequestServices.GetRequiredService<IOperationExecutor>()
            .ExecuteAsync(operation, request!, context, InvocationOrigin.Http, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            AuthorizationInvocationFilter.AuthenticationRequired failure => Problem(failure, StatusCodes.Status401Unauthorized),
            AuthorizationInvocationFilter.PermissionDenied failure => Problem(failure, StatusCodes.Status403Forbidden),
            AuthorizationInvocationFilter.PolicyDenied failure => Problem(failure, StatusCodes.Status400BadRequest),
            _ => result
        };
    }

    private static IResult Problem(IFailure failure, int status)
        => TypedResults.Problem(statusCode: status, type: "about:blank", title: failure.Title, detail: failure.Detail);

    internal static void BindIdentity(this HttpContext http)
    {
        var user = http.RequestServices.GetRequiredService<IUserContext>();
        user.Logout();
        user.User.AddIdentities(http.User.Identities.Select(identity => identity.Clone()));
    }
}
