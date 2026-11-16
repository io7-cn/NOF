using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Adds or excludes filters within a single scope.</summary>
public sealed class InvocationFilterScopeBuilder(IServiceCollection services, InvocationFilterRegistry registry,
    InvocationFilterScope scope, Type? serviceType, string? operationId)
{
    /// <summary>Uses a scoped filter and captures its static description without instantiating it.</summary>
    public InvocationFilterScopeBuilder Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter>(int? order = null)
        where TFilter : class, IInvocationFilter
    {
        registry.Add(new(scope, serviceType, operationId, typeof(TFilter), false, order,
            static provider => provider.GetRequiredService<TFilter>(), static description => TFilter.Describe(description),
            Array.AsReadOnly(TFilter.FailureCases.ToArray())));
        services.TryAddScoped<TFilter>();
        return this;
    }
    /// <summary>Excludes an inherited filter in this scope and its descendants.</summary>
    public InvocationFilterScopeBuilder Exclude<TFilter>() where TFilter : class, IInvocationFilter
    {
        registry.Add(new(scope, serviceType, operationId, typeof(TFilter), true, null, null, null, []));
        return this;
    }
}
