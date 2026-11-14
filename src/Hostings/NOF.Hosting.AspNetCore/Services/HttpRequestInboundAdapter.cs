using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;
/// <summary>HTTP identity and challenge integration, without bulk header propagation.</summary>
public sealed class HttpRequestInboundAdapter(IOperationExecutor executor)
{
    /// <summary>Copies only the identity established by the native HTTP authentication pipeline.</summary>
    public void BindIdentity(HttpContext http)
    {
        var user = http.RequestServices.GetRequiredService<IUserContext>();
        user.Logout();
        user.User.AddIdentities(http.User.Identities.Select(identity => identity.Clone()));
    }

    /// <summary>Invokes business policies; native endpoint authorization owns challenge and forbid responses.</summary>
    public Task<object?> ExecuteAsync(ServiceOperationDescriptor operation, object input, HttpContext http, Context context, CancellationToken ct)
        => executor.ExecuteAsync(operation, input, context, InvocationOrigin.Http, ct);

    /// <summary>Supports explicitly bound native handlers using the same policy boundary.</summary>
    public async Task<object?> InvokeAsync(ServiceOperationDescriptor operation, object input, HttpContext http, Context context, CancellationToken ct)
    {
        BindIdentity(http);
        var result = await ExecuteAsync(operation, input, http, context, ct);
        return result is IFailure failure && result is (AuthorizationInvocationFilter.AuthenticationRequired or AuthorizationInvocationFilter.PermissionDenied or AuthorizationInvocationFilter.PolicyDenied) ? TypedResults.Problem(statusCode: result is AuthorizationInvocationFilter.AuthenticationRequired ? 401 : result is AuthorizationInvocationFilter.PermissionDenied ? 403 : result is AuthorizationInvocationFilter.PolicyDenied ? 400 : 422, type: "about:blank", title: failure.Title, detail: failure.Detail) : result;
    }

}
