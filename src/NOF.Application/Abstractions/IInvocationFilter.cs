namespace NOF.Application;

/// <summary>Continues an application or message invocation with its current context.</summary>
public delegate ValueTask InvocationDelegate(InvocationContext context, object input, CancellationToken cancellationToken);

/// <summary>Defines a transport-independent execution extension. The host decides how it runs.</summary>
public interface IInvocationFilter
{
    /// <summary>Wraps, normalizes, completes or rejects an invocation.</summary>
    ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken);
}

/// <summary>Executes an invocation using the host's chosen extension mechanism.</summary>
public interface IInvocationPipeline
{
    /// <summary>Runs the registered extensions and terminal operation according to host policy.</summary>
    ValueTask ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken);
}
