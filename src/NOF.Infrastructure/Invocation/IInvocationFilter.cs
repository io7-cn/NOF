using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>A host-owned execution extension with static, construction-free failure metadata.</summary>
public interface IInvocationFilter
{
    /// <summary>Gets this filter's generated failure catalog.</summary>
    static virtual IReadOnlyList<InvocationFailureCase> FailureCases => [];
    /// <summary>Selects applicable declared failures; the default includes the entire catalog.</summary>
    static virtual void Describe(InvocationDescriptionContext description) => description.IncludeAll();
    /// <summary>Returns a declared value, or continues to the next stage.</summary>
    ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken);
}
