using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

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
    /// <summary>Gets rules for a generated operation base or a standalone handler.</summary>
    public InvocationFilterScopeBuilder UseCase<THandler>() where THandler : OperationHandler
        => UseCase(services.GetOrAddSingleton<ApplicationServiceRegistry>().GetOperation(typeof(THandler)));
    /// <summary>Gets rules for a stable contract/operation identity.</summary>
    public InvocationFilterScopeBuilder UseCase(OperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new(services, registry, InvocationFilterScope.UseCase, operation.ContractType, operation.Id);
    }
}
