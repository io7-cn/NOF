using NOF.Abstraction;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Application;

/// <summary>Resolves Contract method/interface defaults and rejects conflicting declarations deterministically.</summary>
public static class AuthorizationPolicyResolver
{
    /// <summary>Resolves a Contract interface. Method declarations replace interface defaults; nearer declarations replace inherited defaults.</summary>
    public static AuthorizationPolicy Resolve([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type type, MethodInfo? method = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsInterface || method is not null && (method.DeclaringType?.IsInterface != true || !method.DeclaringType.IsAssignableFrom(type)))
        {
            throw new ArgumentException("Authorization declarations must belong to the selected Contract interface.", nameof(type));
        }
        var owners = type.GetInterfaces().Append(type).OrderBy(owner => owner.FullName, StringComparer.Ordinal).ToArray();
        if (method is not null)
        {
            if (MostSpecific(owners.SelectMany(owner => MatchingMethods(owner, method))) is { } selected)
            {
                return selected;
            }
        }
        return MostSpecific(owners) ?? AuthorizationPolicy.Anonymous;
    }

    private static AuthorizationPolicy? Read(MemberInfo source)
    {
        var values = source.GetCustomAttributes(inherit: false);
        var anonymous = values.OfType<AllowAnonymousAttribute>().Any();
        var authenticated = values.OfType<RequireAuthenticatedAttribute>().Any();
        var groups = values.OfType<IPermissionMetadata>().Select(value => new PermissionRequirement(value.Permissions, value.Match, source)).ToArray();
        if (anonymous && (authenticated || groups.Length > 0))
        {
            throw new InvalidOperationException($"'{source}' combines AllowAnonymous with authorization requirements.");
        }
        return anonymous || authenticated || groups.Length > 0 ? new(authenticated, groups, [source]) : null;
    }

    private static AuthorizationPolicy? MostSpecific(IEnumerable<MemberInfo> members)
    {
        static Type Owner(MemberInfo source) => source as Type ?? source.DeclaringType!;
        var declarations = members.Select(source => (Source: source, Policy: Read(source))).Where(entry => entry.Policy is not null).ToArray();
        // Only an actual descendant declaration can replace an ancestor. A shorter sibling path cannot erase another branch.
        var policies = declarations.Where(entry => !declarations.Any(other => Owner(entry.Source) != Owner(other.Source)
            && Owner(entry.Source).IsAssignableFrom(Owner(other.Source)))).Select(entry => entry.Policy!).ToArray();
        if (policies.Length == 0)
        {
            return null;
        }
        // Interface enumeration and Attribute order never choose a weaker branch.
        if (policies.Skip(1).Any(policy => !Equivalent(policies[0], policy)))
        {
            throw new InvalidOperationException("Ambiguous inherited authorization declarations: "
                + string.Join(", ", policies.SelectMany(policy => policy.Sources).Select(member => $"{member.DeclaringType?.FullName ?? member.Name}.{member.Name}")));
        }
        return new(policies[0].RequiresAuthentication, policies[0].Requirements, policies.SelectMany(policy => policy.Sources));
    }

    private static bool Equivalent(AuthorizationPolicy left, AuthorizationPolicy right)
    {
        static bool Same(PermissionRequirement first, PermissionRequirement second) => first.Match == second.Match
            && first.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(second.Permissions);
        return left.RequiresAuthentication == right.RequiresAuthentication
            && left.Requirements.All(first => right.Requirements.Any(second => Same(first, second)))
            && right.Requirements.All(second => left.Requirements.Any(first => Same(first, second)));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Operation descriptors root Contract methods; generated operation initializers explicitly root inherited interface methods with DynamicDependency. Standalone trimmed callers must likewise preserve their selected Contract method declarations.")]
    private static IEnumerable<MethodInfo> MatchingMethods(Type owner, MethodInfo selected)
        => owner.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == selected.Name && method.IsStatic == selected.IsStatic && method.GetParameters().Select(value => value.ParameterType)
                .SequenceEqual(selected.GetParameters().Select(value => value.ParameterType)));
}
