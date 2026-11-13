using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure;

public sealed class CommandDeliveryExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IObjectSerializer _serializer;
    private readonly CommandHandlerRegistry _commandHandlerRegistry;

    public CommandDeliveryExecutor(
        IServiceProvider serviceProvider,
        IObjectSerializer serializer,
        CommandHandlerRegistry commandHandlerRegistry)
    {
        _serviceProvider = serviceProvider;
        _serializer = serializer;
        _commandHandlerRegistry = commandHandlerRegistry;
    }

    public async ValueTask ExecuteAsync(
        ReadOnlyMemory<byte> payload,
        string handlerTypeName,
        IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken)
    {
        var invoker = ResolveInvoker(handlerTypeName);
        var message = invoker.Bind(payload, _serializer.Deserialize);
        var context = CreateContext(invoker.HandlerType, invoker.MessageType, headers);
        InvocationDelegate terminal = (currentContext, currentMessage, ct)
            => ExecuteCommandHandlerAsync(_serviceProvider, invoker, currentContext, currentMessage, ct);
        await _serviceProvider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(context, message, terminal, cancellationToken).ConfigureAwait(false);
    }

    private ICommandInboundHandlerInvoker ResolveInvoker(string handlerTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerTypeName);

        if (!_commandHandlerRegistry.TryGetInvokerType(handlerTypeName, out var invokerType))
        {
            throw new InvalidOperationException($"Command handler invoker for '{handlerTypeName}' is not registered.");
        }

        return (ICommandInboundHandlerInvoker)_serviceProvider.GetRequiredService(invokerType);
    }

    private static CommandInboundContext CreateContext(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type handlerType,
        Type messageType,
        IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var methodInfo = InboundContextReflection.ResolveHandlerMethodInfo(handlerType, messageType);
        return (CommandInboundContext)new CommandInboundContext
        {
            HandlerMethodInfo = methodInfo,
            HandlerType = handlerType,
            InputType = messageType
        }.WithItems(InboundContextReflection.ReadHeaderItems(headers));
    }

    private static async ValueTask ExecuteCommandHandlerAsync(
        IServiceProvider services,
        ICommandInboundHandlerInvoker invoker,
        InvocationContext context,
        object message,
        CancellationToken cancellationToken)
    {
        using var _ = Context.PushCurrent(context);
        await invoker.InvokeAsync(services, message, context, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class NotificationDeliveryExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IObjectSerializer _serializer;
    private readonly NotificationHandlerRegistry _notificationHandlerRegistry;

    public NotificationDeliveryExecutor(
        IServiceProvider serviceProvider,
        IObjectSerializer serializer,
        NotificationHandlerRegistry notificationHandlerRegistry)
    {
        _serviceProvider = serviceProvider;
        _serializer = serializer;
        _notificationHandlerRegistry = notificationHandlerRegistry;
    }

    public async ValueTask ExecuteAsync(
        ReadOnlyMemory<byte> payload,
        string handlerTypeName,
        IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken)
    {
        var invoker = ResolveInvoker(handlerTypeName);
        var message = invoker.Bind(payload, _serializer.Deserialize);
        var context = CreateContext(invoker.HandlerType, invoker.MessageType, headers);
        InvocationDelegate terminal = (currentContext, currentMessage, ct)
            => ExecuteNotificationHandlerAsync(_serviceProvider, invoker, currentContext, currentMessage, ct);
        await _serviceProvider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(context, message, terminal, cancellationToken).ConfigureAwait(false);
    }

    private INotificationInboundHandlerInvoker ResolveInvoker(string handlerTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerTypeName);

        if (!_notificationHandlerRegistry.TryGetInvokerType(handlerTypeName, out var invokerType))
        {
            throw new InvalidOperationException($"Notification handler invoker for '{handlerTypeName}' is not registered.");
        }

        return (INotificationInboundHandlerInvoker)_serviceProvider.GetRequiredService(invokerType);
    }

    private static NotificationInboundContext CreateContext(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type handlerType,
        Type messageType,
        IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var methodInfo = InboundContextReflection.ResolveHandlerMethodInfo(handlerType, messageType);
        return (NotificationInboundContext)new NotificationInboundContext
        {
            HandlerMethodInfo = methodInfo,
            HandlerType = handlerType,
            InputType = messageType
        }.WithItems(InboundContextReflection.ReadHeaderItems(headers));
    }

    private static async ValueTask ExecuteNotificationHandlerAsync(
        IServiceProvider services,
        INotificationInboundHandlerInvoker invoker,
        InvocationContext context,
        object message,
        CancellationToken cancellationToken)
    {
        using var _ = Context.PushCurrent(context);
        await invoker.InvokeAsync(services, message, context, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ApplicationServiceExecutionAdapter
{
    private readonly IServiceProvider _serviceProvider;

    public ApplicationServiceExecutionAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async ValueTask<RequestInboundContext> ExecuteAsync(
        object request,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type handlerType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type responseType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string methodName,
        IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken,
        Context? localContext = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = CreateContext(request, handlerType, responseType, serviceType, methodName, headers);
        if (localContext is not null)
        {
            context = (RequestInboundContext)context.WithItems(localContext.Items);
            context.IsLocalInvocation = true;
        }
        InvocationDelegate terminal = (currentContext, currentRequest, ct)
            => ExecuteRequestHandlerAsync(_serviceProvider, handlerType, (RequestInboundContext)currentContext, currentRequest, ct);
        await _serviceProvider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(context, request, terminal, cancellationToken).ConfigureAwait(false);
        return context;
    }

    private static RequestInboundContext CreateContext(
        object request,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        Type handlerType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
        Type responseType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string methodName,
        IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var serviceMethodInfo = InboundContextReflection.ResolveServiceMethodInfo(serviceType, methodName);
        var requestType = InboundContextReflection.GetHandlerRequestType(handlerType);
        if (!requestType.IsInstanceOfType(request))
        {
            throw new InvalidOperationException($"Request '{request.GetType().FullName}' is not assignable to '{requestType.FullName}'.");
        }

        var handlerMethodInfo = InboundContextReflection.ResolveHandlerMethodInfo(handlerType, requestType);
        return (RequestInboundContext)new RequestInboundContext
        {
            ServiceType = serviceType,
            ServiceMethodInfo = serviceMethodInfo,
            HandlerType = handlerType,
            HandlerMethodInfo = handlerMethodInfo,
            InputType = requestType,
            ResponseType = responseType
        }.WithItems(InboundContextReflection.ReadHeaderItems(headers));
    }

    private static async ValueTask ExecuteRequestHandlerAsync(
        IServiceProvider services,
        Type handlerType,
        RequestInboundContext context,
        object request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var _ = Context.PushCurrent(context);
        var handler = (ServiceOperation)services.GetRequiredService(handlerType);
        var response = await handler.HandleAsync(request, context, cancellationToken).ConfigureAwait(false);
        context.SetResponse(response);
    }

}
/// <summary>Resolves handler and service metadata for inbound execution contexts.</summary>

public static class InboundContextReflection
{
    /// <summary>Decodes transported message headers into execution items.</summary>
    public static IReadOnlyDictionary<object, object?> ReadHeaderItems(IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var items = new Dictionary<object, object?>();
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    items[key] = value;
                }
            }
        }
        return items;
    }

    public static MethodInfo ResolveServiceMethodInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string methodName)
        => serviceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Unable to resolve service method '{serviceType.FullName}.{methodName}'.");

    public static MethodInfo ResolveHandlerMethodInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type handlerType,
        Type messageType)
    {
        var methods = handlerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var method in methods)
        {
            if (!string.Equals(method.Name, nameof(CommandHandler.HandleAsync), StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 3)
            {
                continue;
            }

            if (parameters[0].ParameterType == messageType
                && parameters[1].ParameterType == typeof(Context)
                && parameters[2].ParameterType == typeof(CancellationToken))
            {
                return method;
            }
        }

        throw new InvalidOperationException($"Unable to resolve handler method for '{handlerType.FullName}' and message type '{messageType.FullName}'.");
    }

    public static Type GetHandlerRequestType(Type handlerType)
    {
        for (var current = handlerType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ServiceOperation<,>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"Unable to resolve application service request type from handler '{handlerType.FullName}'.");
    }
}
