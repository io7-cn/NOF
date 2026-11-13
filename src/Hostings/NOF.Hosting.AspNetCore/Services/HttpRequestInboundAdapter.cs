using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>Owns HTTP metadata, challenges and the bridge to host operation execution.</summary>
public sealed class HttpRequestInboundAdapter(IOperationExecutor executor,
    IOptions<HttpRequestHeaderOptions> headerOptions, IOptions<AuthenticationResourceServerOptions> authenticationOptions)
{
    /// <summary>Runs one operation in the existing HTTP request scope.</summary>
    public async Task<object?> InvokeAsync(ServiceOperationDescriptor operation, object input,
        HttpContext http, CancellationToken cancellationToken)
    {
        var items = new Dictionary<object, object?>();
        foreach (var header in http.Request.Headers)
        {
            if (!string.Equals(header.Key, NOFAbstractionConstants.Transport.Headers.TraceParent, StringComparison.OrdinalIgnoreCase)
                && headerOptions.Value.AllowedHeaders.Any(pattern => header.Key.MatchWildcard(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                items[header.Key] = header.Value.ToString();
            }
        }
        var result = await executor.ExecuteAsync(operation, input, Context.Empty.WithItems(items), InvocationOrigin.Http, cancellationToken).ConfigureAwait(false);
        if (result is AuthorizationInvocationFilter.AuthenticationRequired or AuthorizationInvocationFilter.PermissionDenied)
        {
            var forbidden = result is AuthorizationInvocationFilter.PermissionDenied;
            var parameters = new List<string> { forbidden ? "error=\"insufficient_scope\"" : "error=\"invalid_token\"" };
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
        if (result is AuthorizationInvocationFilter.AuthenticationRequired or AuthorizationInvocationFilter.PermissionDenied or AuthorizationInvocationFilter.PolicyDenied)
        {
            return ServiceRejectionHttpFilter.ToProblem(result);
        }
        return result;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
