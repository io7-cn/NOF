using NOF.Application;
using NOF.Contract;
using System.Reflection;

namespace NOF.Infrastructure;

/// <summary>Declaration-only authorization requirements shared by execution and description.</summary>
public sealed record InvocationAuthorizationPolicy(IReadOnlyList<string?> Requirements,
    bool RequireAuthenticatedTenant = false, bool RejectMismatchedTenant = false, bool CustomAuthorization = false)
{
    /// <summary>Gets whether authentication can be required by this operation or host policy.</summary>
    public bool RequiresAuthentication => CustomAuthorization || RequireAuthenticatedTenant || Requirements.Any(value => value is not null);
    /// <summary>Gets whether permissions or tenant trust can reject an authenticated caller.</summary>
    public bool RequiresAuthorization => CustomAuthorization || RequireAuthenticatedTenant || RejectMismatchedTenant || Requirements.Any(value => !string.IsNullOrEmpty(value));
    /// <summary>Reads contract and implementation precedence without constructing handlers.</summary>
    public static InvocationAuthorizationPolicy Resolve(Type? serviceType, MethodInfo? serviceMethod,
        Type handlerType, MethodInfo handlerMethod, TenantAuthorizationOptions? options = null, bool customAuthorization = false)
    {
        var input = serviceType is null || serviceMethod is null ? null : Requirement(serviceMethod) ?? Requirement(serviceType);
        var implementation = Requirement(handlerMethod) ?? Requirement(handlerType);
        return new(Array.AsReadOnly(new[] { input?.Permission, implementation?.Permission }),
            options?.RequireAuthenticatedTenant ?? false, options?.RejectMismatchedTenant ?? false, customAuthorization);
    }
    private static PermissionValue? Requirement(MemberInfo member)
    {
        var attribute = member.GetCustomAttributes(inherit: true).OfType<MetadataAttribute>()
            .LastOrDefault(item => string.Equals(item.Key, RequirePermissionAttribute.MetadataKey, StringComparison.OrdinalIgnoreCase));
        return attribute is null ? null : new(attribute.Value);
    }
    private readonly record struct PermissionValue(string? Permission);
}
