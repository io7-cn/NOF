using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using System.Diagnostics;

namespace NOF.Infrastructure;

public sealed class CommandSender : ICommandSender
{
    private readonly ICommandRider _rider;
    private readonly MessageDelivery _delivery;
    private readonly IOutboxStore _outbox;
    private readonly IObjectSerializer _objectSerializer;
    private readonly OutboxOrderSequenceAllocator _orderSequenceAllocator;

    public CommandSender(
        ICommandRider rider,
        MessageDelivery delivery,
        IOutboxStore outbox,
        IObjectSerializer objectSerializer,
        OutboxOrderSequenceAllocator orderSequenceAllocator)
    {
        _rider = rider;
        _delivery = delivery;
        _outbox = outbox;
        _objectSerializer = objectSerializer;
        _orderSequenceAllocator = orderSequenceAllocator;
    }

    public async Task DeferSendAsync(object command, Type commandType, Context context, CancellationToken cancellationToken = default)
        => await DeferSendCoreAsync(command, commandType, null, context, false, cancellationToken);

    public async Task DeferSendOrderedAsync(
        object command,
        Type commandType,
        string orderKey,
        Context context,
        bool completesOrderKey = false,
        CancellationToken cancellationToken = default)
        => await DeferSendCoreAsync(command, commandType, orderKey, context, completesOrderKey, cancellationToken);

    private async Task DeferSendCoreAsync(
        object command,
        Type commandType,
        string? orderKey,
        Context context,
        bool completesOrderKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(context);
        await _delivery.ExecuteAsync(command, context, async (headers, token) =>
        {
            var dispatchRoutes = _objectSerializer.SerializeToText(
                new[] { commandType.DisplayName },
                typeof(string[]));

            var order = orderKey is null
                ? (OutboxOrder?)null
                : await _orderSequenceAllocator.AllocateAsync(orderKey, completesOrderKey, token);

            _outbox.Stage(NOFOutboxMessage.Create(
                OutboxMessageType.Command,
                dispatchRoutes,
                _objectSerializer.Serialize(command).ToArray(),
                _objectSerializer.SerializeToText(headers, typeof(Dictionary<string, string?>)),
                Activity.Current?.ToTraceParent(),
                order));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(object command, Type commandType, Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(context);
        await _delivery.ExecuteAsync(command, context, async (headers, token) =>
        {
            var payload = _objectSerializer.Serialize(command, command.GetType());
            await _rider.SendAsync(payload, commandType.DisplayName, headers, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

}
