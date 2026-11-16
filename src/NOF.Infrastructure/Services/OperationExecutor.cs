using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

namespace NOF.Infrastructure;

/// <summary>The single execution boundary for local calls, HTTP and reliable message consumers.</summary>
public sealed class OperationExecutor(IServiceProvider services, ApplicationServiceRegistry registry) : IOperationExecutor
{
    /// <inheritdoc />
    public async Task<object?> ExecuteAsync(OperationDescriptor operation, object input,
        Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.IsOneWay)
        {
            throw new InvalidOperationException("One-way operations must be submitted through a transport-owned acceptance endpoint, not a reply-producing local invocation.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out _))
        {
            context = context.WithTenantId(Context.Current.TenantId);
        }
        var caller = services.GetRequiredService<IUserContext>();
        var identities = caller.User.Identities.Select(identity => identity.Clone()).ToArray();
        await using var scope = services.CreateAsyncScope();
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        if (!ReferenceEquals(caller, user))
        {
            user.Logout();
            user.User.AddIdentities(identities);
        }
        var result = await scope.ServiceProvider.GetRequiredService<OperationExecutor>()
            .ExecuteInScopeAsync(operation, input, context, InvocationOrigin.Local, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Executes in the host-owned scope without creating a second transaction or identity scope.</summary>
    public async Task<object?> ExecuteInScopeAsync(OperationDescriptor operation, object input,
        Context context, InvocationOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var binding = services.BindNOF(context);
        var mapping = registry.GetMapping(operation);
        if (mapping.RequestType != operation.RequestType || mapping.ReturnType != operation.ResponseType
            || !operation.RequestType.IsInstanceOfType(input))
        {
            throw new InvalidOperationException($"The operation descriptor does not match '{operation.Id}'.");
        }
        var implementationType = services.GetRequiredService<ApplicationServiceRegistry>().GetOperationImplementation(mapping.HandlerType);
        var invocation = new InvocationContext
        {
            Operation = operation,
            HandlerType = implementationType,
            HandlerMethodInfo = InboundContextReflection.ResolveHandlerMethodInfo(implementationType, mapping.RequestType),
            Origin = origin
        }.WithContext(context);
        var pipeline = services.GetRequiredService<IInvocationPipeline>();
        var plan = pipeline.Describe(invocation);
        var result = await pipeline.ExecuteAsync(invocation, input, async (current, value, ct) =>
        {
            var handler = (OperationHandler)services.GetRequiredService(mapping.HandlerType);
            var business = await handler.HandleAsync(value, current, ct).ConfigureAwait(false);
            InvocationResult.ValidateInitialization(business);
            if (operation.IsOneWay ? business is not null : business is null ? !operation.AllowsNullResponse : !operation.ResponseType.IsInstanceOfType(business))
            {
                throw new InvalidOperationException($"The business response does not match '{operation.ResponseType}'.");
            }
            return business;
        }, cancellationToken).ConfigureAwait(false);
        return plan.Validate(result);
    }
}
