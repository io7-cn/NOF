using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure;

/// <summary>Combines the Contract authorization snapshot with explicit host tenant and custom-provider constraints.</summary>
public sealed record InvocationAuthorizationPolicy(AuthorizationPolicy Authorization,
    bool RequireAuthenticatedTenant = false, bool RejectMismatchedTenant = false, bool CustomAuthorization = false)
{
    /// <summary>Gets whether authentication can be required by the operation or host.</summary>
    public bool RequiresAuthentication => CustomAuthorization || RequireAuthenticatedTenant || Authorization.RequiresAuthentication;
    /// <summary>Gets whether permissions or tenant trust can reject an authenticated caller.</summary>
    public bool RequiresAuthorization => CustomAuthorization || RequireAuthenticatedTenant || RejectMismatchedTenant || Authorization.Requirements.Count > 0;
    /// <summary>Resolves the Contract declaration without reading or constructing handlers.</summary>
    public static InvocationAuthorizationPolicy Resolve([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type? serviceType,
        MethodInfo? serviceMethod, TenantAuthorizationOptions? options = null, bool customAuthorization = false)
        => new(serviceType is null ? AuthorizationPolicy.Anonymous : AuthorizationPolicyResolver.Resolve(serviceType, serviceMethod),
            options?.RequireAuthenticatedTenant ?? false, options?.RejectMismatchedTenant ?? false, customAuthorization);
}
