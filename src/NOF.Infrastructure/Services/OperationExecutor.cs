using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure;

/// <summary>The single execution boundary for local calls, HTTP and reliable message consumers.</summary>
public sealed class OperationExecutor(IServiceProvider services, ApplicationServiceRegistry registry) : IOperationExecutor
{
    /// <inheritdoc />
    public async Task<object?> ExecuteAsync(ServiceOperationDescriptor operation, object input,
        Context context, InvocationOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var binding = services.BindNOF(context);
        var mapping = registry.GetRegistration(operation.ServiceType).HandlerMappings.GetValueOrDefault(operation.OperationName)
            ?? throw new InvalidOperationException($"No handler mapping for '{operation.Id}'.");
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
            var handler = (ServiceOperation)services.GetRequiredService(mapping.HandlerType);
            var business = await handler.HandleAsync(value, current, ct).ConfigureAwait(false);
            InvocationResult.ValidateInitialization(business);
            if (business is null ? !operation.AllowsNullResponse : !operation.ResponseType.IsInstanceOfType(business))
            {
                throw new InvalidOperationException($"The business response does not match '{operation.ResponseType}'.");
            }
            return business;
        }, cancellationToken).ConfigureAwait(false);
        return plan.Validate(result);
    }
}

/// <summary>Resolves preserved operation policy metadata without invoking reflection-based handlers.</summary>
public static class InboundContextReflection
{
    /// <summary>Decodes permitted transport headers into semantic items.</summary>
    public static IReadOnlyDictionary<object, object?> ReadHeaderItems(IEnumerable<KeyValuePair<string, string?>>? headers)
        => headers?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value) ?? [];

    /// <summary>Resolves contract policy metadata.</summary>
    public static MethodInfo ResolveServiceMethodInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType, string name)
        => serviceType.GetMethod(name) ?? throw new InvalidOperationException($"Unknown operation '{name}'.");

    /// <summary>Resolves the typed implementation method for policy metadata.</summary>
    public static MethodInfo ResolveHandlerMethodInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type handlerType,
        Type inputType)
        => handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == nameof(ServiceOperation.HandleAsync)
                && method.GetParameters() is [{ ParameterType: var input }, { ParameterType: var context }, { ParameterType: var token }]
                && input == inputType && context == typeof(InvocationContext) && token == typeof(CancellationToken))
            ?? throw new InvalidOperationException($"No typed operation method on '{handlerType}'.");
}
