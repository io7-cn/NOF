using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

public interface IApplicationServiceImplementation : IApplicationServiceType
{
    static abstract IReadOnlyDictionary<string, OperationMapping> HandlerMappings { get; }
}
