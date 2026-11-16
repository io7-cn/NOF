using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Continues execution; business and filter values travel along the same return path.</summary>
public delegate ValueTask<object?> InvocationDelegate(InvocationContext context, object input, CancellationToken cancellationToken);
