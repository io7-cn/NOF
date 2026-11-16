using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>One Memory adapter endpoint targeting a one-way operation.</summary>
public sealed record MemoryEndpoint(string Route, string ConsumerId, Type MessageType,
    OperationDescriptor Operation, bool IsSubscription);
