using NOF.Contract;
using System.Reflection;

namespace NOF.Application;

/// <summary>Interprets operation descriptors and Contract policy metadata at application boundaries.</summary>
public static class ServiceOperationInspector
{
    /// <summary>Checks that message execution can discard no business output or declared failure.</summary>
    public static bool CanSubmit(ServiceOperationDescriptor operation)
        => operation.ResponseType == typeof(Empty)
            && operation.MethodInfo.ReturnType == typeof(Empty)
            && !operation.MethodInfo.IsDefined(typeof(FailureCaseAttribute), inherit: false);

    /// <summary>Reads host-tenant selection without embedding policy interpretation in Contract attributes.</summary>
    public static bool RequiresHostTenant(MemberInfo? method)
        => method?.GetCustomAttributes<MetadataAttribute>(inherit: true)
            .Any(static attribute => string.Equals(attribute.Key, UseHostTenantAttribute.MetadataKey, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(attribute.Value, out var enabled) && enabled) == true;
}
