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

}
