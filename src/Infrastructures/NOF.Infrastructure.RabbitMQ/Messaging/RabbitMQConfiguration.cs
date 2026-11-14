using NOF.Application;
using NOF.Contract;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>One queue consumer, independent of its native exchange topology.</summary>
public sealed record RabbitMQEndpoint(string QueueName, string ConsumerId, ServiceOperationDescriptor Operation,
    Func<IChannel, CancellationToken, Task>? ConfigureChannel = null, bool RequeueOnFailure = true);

/// <summary>An immutable configuration selected at host startup.</summary>
public sealed record RabbitMQConfigurationSnapshot(IReadOnlyList<Func<IChannel, CancellationToken, Task>> Topologies,
    IReadOnlyList<RabbitMQEndpoint> Endpoints);

/// <summary>Accumulates host-local topology callbacks and endpoints until startup freezes them.</summary>
public sealed class RabbitMQConfiguration
{
    private readonly Lock _gate = new();
    private readonly List<Func<IChannel, CancellationToken, Task>> _topologies = [];
    private readonly List<RabbitMQEndpoint> _endpoints = [];
    private RabbitMQConfigurationSnapshot? _snapshot;

    /// <summary>Appends a native topology callback; callbacks execute in registration order.</summary>
    public void AddTopology(Func<IChannel, CancellationToken, Task> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            EnsureMutable();
            _topologies.Add(configure);
        }
    }

    /// <summary>Adds a queue endpoint, rejecting duplicate identities and discarded business results.</summary>
    public void AddEndpoint(RabbitMQEndpoint endpoint)
    {
        lock (_gate)
        {
            EnsureMutable();
            if (endpoint.Operation.ResponseType != typeof(Empty) || !ServiceOperationInspector.CanSubmit(endpoint.Operation)
                || _endpoints.Any(item => item.QueueName == endpoint.QueueName || item.ConsumerId == endpoint.ConsumerId))
            {
                throw new InvalidOperationException("Each RabbitMQ queue must select one Empty operation without declared failures.");
            }
            _endpoints.Add(endpoint);
        }
    }

    /// <summary>Freezes all declarations together without executing user callbacks.</summary>
    public RabbitMQConfigurationSnapshot Freeze()
    {
        lock (_gate)
        {
            return _snapshot ??= new(Array.AsReadOnly(_topologies.ToArray()), Array.AsReadOnly(_endpoints.ToArray()));
        }
    }

    private void EnsureMutable()
    {
        if (_snapshot is not null)
        {
            throw new InvalidOperationException("RabbitMQ configuration is frozen after startup.");
        }
    }
}
