namespace NOF.Sample.Application;

/// <summary>Outbound message client contract; the host supplies transport and serialization.</summary>
public interface IMessageClient
{
    ValueTask PublishAsync<T>(string exchange, string routingKey, T message,
        IReadOnlyDictionary<string, string?> headers, CancellationToken cancellationToken);
}
