using NOF.Contract;
using System.Reflection;

namespace NOF.Application;

/// <summary>Interprets declarative Contract references at an application execution or submission boundary.</summary>
public static class ServiceOperationInspector
{
    /// <summary>Resolves a declared operation without constructing its handler.</summary>
    public static MethodInfo ResolveMethod(ServiceOperationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.ServiceType.GetMethod(reference.OperationName)
            ?? throw new ArgumentException($"Operation '{reference.Id}' does not exist.", nameof(reference));
    }

    /// <summary>Validates the declared input against its typed reference.</summary>
    public static MethodInfo ValidateReference(ServiceOperationReference reference)
    {
        var method = ResolveMethod(reference);
        if (method.GetParameters() is not [{ ParameterType: var input }] || input != reference.RequestType)
        {
            throw new ArgumentException("The operation input must match its contract.", nameof(reference));
        }
        return method;
    }

    /// <summary>Checks whether asynchronous submission can discard no business output or declared failure.</summary>
    public static bool CanSubmit(ServiceOperationReference reference)
    {
        var method = ValidateReference(reference);
        return method.ReturnType == typeof(Empty) && !method.IsDefined(typeof(FailureCaseAttribute), inherit: false);
    }

    /// <summary>Reads host-tenant selection without embedding policy interpretation in Contract attributes.</summary>
    public static bool RequiresHostTenant(MemberInfo? method)
        => method?.GetCustomAttributes<MetadataAttribute>(inherit: true)
            .Any(static attribute => string.Equals(attribute.Key, UseHostTenantAttribute.MetadataKey, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(attribute.Value, out var enabled) && enabled) == true;
}
