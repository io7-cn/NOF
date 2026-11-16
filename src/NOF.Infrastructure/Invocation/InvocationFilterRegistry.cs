using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Host-local application rules frozen when execution plans are built.</summary>
public sealed class InvocationFilterRegistry : Registry<InvocationFilterRule>
{
    /// <inheritdoc />
    public override void Add(InvocationFilterRule item)
    {
        var existing = Items.ToList().FindIndex(rule => rule.Scope == item.Scope && rule.ServiceType == item.ServiceType
            && rule.OperationId == item.OperationId && rule.FilterType == item.FilterType);
        if (Items.IsFrozen)
        {
            throw new InvalidOperationException("Invocation rules are frozen.");
        }
        if (existing >= 0)
        {
            var previous = Items[existing];
            Items[existing] = item with { Order = item.Order ?? previous.Order };
            return;
        }
        base.Add(item);
    }

    /// <summary>Resolves only applicable rules, preserving outer positions and explicit re-enablement.</summary>
    public IReadOnlyList<InvocationFilterRegistration> Resolve(Type? serviceType, string operationId)
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
