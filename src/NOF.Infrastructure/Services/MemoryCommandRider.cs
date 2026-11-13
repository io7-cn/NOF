using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Infrastructure;

public sealed class MemoryCommandRider : ICommandRider
{
    private readonly CommandHandlerRegistry _commandHandlerRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IObjectSerializer _objectSerializer;

    public MemoryCommandRider(
        CommandHandlerRegistry commandHandlerRegistry,
        IServiceProvider serviceProvider,
        IObjectSerializer objectSerializer)
    {
        _commandHandlerRegistry = commandHandlerRegistry;
        _serviceProvider = serviceProvider;
        _objectSerializer = objectSerializer;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> payload,
        string messageRoute,
        IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken = default)
    {
        var route = ResolveRoute(messageRoute);

        var messageId = ResolveMessageId(headers);
        await EnqueueAsync(
            messageId,
            InboxMessageType.Command,
            payload,
            route,
            headers,
            cancellationToken);
    }

    private string ResolveRoute(string messageRoute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageRoute);

        var resolvedHandlerType = _commandHandlerRegistry.GetHandlers(messageRoute).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"In-memory transport cannot route command '{messageRoute}'. No matching local handler registered.");

        return resolvedHandlerType.DisplayName;
    }

    private async Task<bool> EnqueueAsync(
        Guid messageId,
        InboxMessageType messageType,
        ReadOnlyMemory<byte> payload,
        string route,
        IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();

        var dbContext = scope.ServiceProvider.GetService<IInboxStore>();
        if (dbContext is null)
        {
            return true;
        }

        var order = InboxMessageOrdering.Parse(headers);
        var message = NOFInboxMessage.Create(
            messageId,
            messageType,
            route,
            payload.ToArray(),
            SerializeHeaders(headers),
            order);

        try
        {
            await dbContext.ReceiveAsync(message, order, cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private string SerializeHeaders(IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var dictionary = headers?.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)
            ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        return _objectSerializer.SerializeToText(dictionary, typeof(Dictionary<string, string?>));
    }

    private static Guid ResolveMessageId(IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var messageIdValue = headers?.FirstOrDefault(static kvp => kvp.Key == NOFAbstractionConstants.Transport.Headers.MessageId).Value;
        return Guid.TryParse(messageIdValue, out var messageId) ? messageId : Guid.NewGuid();
    }
}
