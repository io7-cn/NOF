using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;
/// <summary>HTTP identity and challenge integration, without bulk header propagation.</summary>
public sealed class HttpRequestInboundAdapter(IOperationExecutor executor, IOptions<AuthenticationResourceServerOptions> authenticationOptions)
{
    /// <summary>Establishes the native boundary identity from a verified principal or explicit credential sources.</summary>
    public async Task AuthenticateAsync(HttpContext http, InvocationPlan plan, CancellationToken ct)
    {
        var user = http.RequestServices.GetRequiredService<IUserContext>();
        if (http.User.Identity?.IsAuthenticated == true)
        {
            user.Logout();
            user.User.AddIdentities(http.User.Identities.Select(identity => identity.Clone()));
            return;
        }

        if (!plan.Filters.Any(filter => filter.FilterType == typeof(AuthenticationInvocationFilter)))
        {
            return;
        }

        var credentials = Context.Empty;
        foreach (var source in authenticationOptions.Value.Sources)
        {
            if (http.Request.Headers.TryGetValue(source.HeaderName, out var value))
            {
                credentials = credentials.WithItem(source.HeaderName, value.ToString());
            }
        }

        await http.RequestServices.GetRequiredService<AuthenticationInvocationFilter>().AuthenticateContextAsync(credentials, ct);
        http.User = new System.Security.Claims.ClaimsPrincipal(user.User.Identities.Select(identity => identity.Clone()));
    }

    /// <summary>Invokes with the boundary's explicit semantic snapshot.</summary>
    public async Task<object?> ExecuteAsync(ServiceOperationDescriptor operation, object input, HttpContext http, Context context, CancellationToken ct)
    {
        var result = await executor.ExecuteAsync(operation, input, context, InvocationOrigin.Http, ct);
        if (result is AuthorizationInvocationFilter.AuthenticationRequired or AuthorizationInvocationFilter.PermissionDenied)
        {
            var parameters = new List<string>
            {
                result is AuthorizationInvocationFilter.PermissionDenied ? "error=\"insufficient_scope\"" : "error=\"invalid_token\""
            };
            if (authenticationOptions.Value.AuthorizationServerIssuer is { Length: > 0 } issuer)
            {
                parameters.Add($"authorization_server=\"{Escape(issuer)}\"");
            }

            if (result is AuthorizationInvocationFilter.PermissionDenied denied && denied.Error.RequiredPermissions.Count > 0)
            {
                parameters.Add($"scope=\"{Escape(string.Join(' ', denied.Error.RequiredPermissions))}\"");
            }

            http.Response.Headers.WWWAuthenticate = $"Bearer {string.Join(", ", parameters)}";
        }

        return result;
    }

    /// <summary>Supports explicitly bound native handlers using the same policy boundary.</summary>
    public async Task<object?> InvokeAsync(ServiceOperationDescriptor operation, object input, HttpContext http, CancellationToken ct)
    {
        var plan = http.RequestServices.GetRequiredService<IInvocationPlanProvider>().Describe(operation, InvocationOrigin.Http);
        await AuthenticateAsync(http, plan, ct);
        var result = await ExecuteAsync(operation, input, http, Context.Empty, ct);
        return result is IFailure failure ? TypedResults.Problem(statusCode: result is AuthorizationInvocationFilter.AuthenticationRequired ? 401 : result is AuthorizationInvocationFilter.PermissionDenied ? 403 : result is AuthorizationInvocationFilter.PolicyDenied ? 400 : 422, type: "about:blank", title: failure.Title, detail: failure.Detail) : result;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
