using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>One immutable Use/Exclude declaration at a specific application scope.</summary>
public sealed record InvocationFilterRule(InvocationFilterScope Scope, Type? ServiceType, string? OperationId,
    Type FilterType, bool Excluded, int? Order, Func<IServiceProvider, IInvocationFilter>? Resolve,
    Action<InvocationDescriptionContext>? Describe, IReadOnlyList<InvocationFailureCase> FailureCases);

/// <summary>Host-local application rules frozen when execution plans are built.</summary>
public sealed class InvocationFilterRegistry : Registry<InvocationFilterRule>
{
    /// <inheritdoc />
    public override void Add(InvocationFilterRule item)
    {
        var existing = Items.ToList().FindIndex(rule => rule.Scope == item.Scope && rule.ServiceType == item.ServiceType
            && rule.OperationId == item.OperationId && rule.FilterType == item.FilterType);
        if (Items.IsFrozen)
        { throw new InvalidOperationException("Invocation rules are frozen."); }
        if (existing >= 0)
        {
            var previous = Items[existing];
            Items[existing] = item with { Order = item.Order ?? previous.Order };
            return;
        }
        base.Add(item);
    }

    /// <summary>Resolves only applicable rules, preserving outer positions and explicit re-enablement.</summary>
    public IReadOnlyList<InvocationFilterRegistration> Resolve(Type serviceType, string operationId)
    {
        var rules = Freeze();
        var active = new Dictionary<Type, InvocationFilterRegistration>();
        foreach (var scope in new[] { InvocationFilterScope.Global, InvocationFilterScope.Service, InvocationFilterScope.UseCase })
        {
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                if (rule.Scope != scope || rule.ServiceType is not null && rule.ServiceType != serviceType
                    || rule.OperationId is not null && rule.OperationId != operationId)
                {
                    continue;
                }
                if (rule.Excluded)
                {
                    active.Remove(rule.FilterType);
                }
                else if (active.TryGetValue(rule.FilterType, out var inherited))
                {
                    active[rule.FilterType] = inherited with { Order = rule.Order ?? inherited.Order };
                }
                else
                {
                    active.Add(rule.FilterType, new(rule.FilterType, rule.Resolve!, rule.Describe!, rule.FailureCases,
                        scope, rule.Order ?? 0, index));
                }
            }
        }
        return Array.AsReadOnly(active.Values.OrderBy(item => item.Scope).ThenBy(item => item.Order).ThenBy(item => item.Sequence).ToArray());
    }
}

/// <summary>Configures the three application scopes without referencing handler implementation classes.</summary>
public sealed class InvocationFiltersBuilder(IServiceCollection services, InvocationFilterRegistry registry)
{
    /// <summary>Gets rules applied to every operation.</summary>
    public InvocationFilterScopeBuilder Global => new(services, registry, InvocationFilterScope.Global, null, null);
    /// <summary>Gets rules for one service contract.</summary>
    public InvocationFilterScopeBuilder Service<TService>() where TService : IApplicationService
    {
        if (!typeof(TService).IsInterface)
        {
            throw new ArgumentException("Filter service scopes must name an application service interface.");
        }
        return new(services, registry, InvocationFilterScope.Service, typeof(TService), null);
    }
    /// <summary>Gets rules for a stable contract/operation identity.</summary>
    public InvocationFilterScopeBuilder UseCase(ServiceOperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new(services, registry, InvocationFilterScope.UseCase, operation.ServiceType, operation.Id);
    }
}

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
