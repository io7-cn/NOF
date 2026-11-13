using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure.Tests;

/// <summary>Supplies runtime metadata for focused filter tests that do not run generated contracts.</summary>
public sealed class InvocationTestDescriptor(Type service, string name, Type input, Type output) : ServiceOperationDescriptor(service, name, false)
{
    public override Type RequestType => input;
    public override Type ResponseType => output;
}

public static class InvocationTestExtensions
{
    public static Task<object?> InvokeAsync<TService>(this IOperationInvoker invoker, string name, object input,
        Context context, CancellationToken cancellationToken = default)
        => invoker.InvokeAsync(new InvocationTestDescriptor(typeof(TService), name, input.GetType(), typeof(TService).GetMethod(name)!.ReturnType),
            input, context, cancellationToken);
}
