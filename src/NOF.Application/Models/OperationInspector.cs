using NOF.Contract;
using System.Reflection;

namespace NOF.Application;

/// <summary>Interprets operation descriptors and Contract policy metadata at application boundaries.</summary>
public static class OperationInspector
{
    /// <summary>Checks that only an explicitly one-way operation can enter a message endpoint.</summary>
    public static bool CanSubmit(OperationDescriptor operation)
        => operation.IsOneWay
            && (operation.ContractType is null || operation.MethodInfo.ReturnType == typeof(void)
                && !operation.MethodInfo.IsDefined(typeof(FailureCaseAttribute), inherit: false));

}
