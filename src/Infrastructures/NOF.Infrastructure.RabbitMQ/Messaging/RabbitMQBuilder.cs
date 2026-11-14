using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Fluently configures native topology callbacks and operation consumers in independent feature modules.</summary>
public sealed class RabbitMQBuilder
{
    private readonly IServiceCollection _services;
    private readonly Lock _gate = new();
    private readonly List<Func<IChannel, CancellationToken, Task>> _topologies = [];
    private readonly List<RabbitMQEndpoint> _endpoints = [];
    private bool _frozen;

    /// <summary>Creates the host-local fluent configuration.</summary>
    public RabbitMQBuilder(IServiceCollection services)
    {
        _services = services;
        Topologies = _topologies.AsReadOnly();
        Endpoints = _endpoints.AsReadOnly();
    }

    /// <summary>Gets native topology callbacks in declaration order.</summary>
    public IReadOnlyList<Func<IChannel, CancellationToken, Task>> Topologies { get; }
    /// <summary>Gets mapped queue endpoints.</summary>
    public IReadOnlyList<RabbitMQEndpoint> Endpoints { get; }

    /// <summary>Prevents further declarations before startup consumes this configuration.</summary>
    public RabbitMQBuilder Freeze()
    {
        lock (_gate)
        {
            _frozen = true;
            return this;
        }
    }
    /// <summary>Appends native RabbitMQ topology work to run before every consumer starts.</summary>
    public RabbitMQBuilder Topology(Func<IChannel, CancellationToken, Task> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            EnsureMutable();
            _topologies.Add(configure);
            return this;
        }
    }

    /// <summary>Maps a statically selected operation to a competing-consumer queue.</summary>
    public RabbitMQBuilder MapQueue<TOperation>(string queue, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true) where TOperation : IServiceOperationDefinition
        => Add(queue, TOperation.Descriptor, configureChannel, requeueOnFailure);

    /// <summary>Maps an explicitly typed Empty operation to a queue.</summary>
    public RabbitMQBuilder MapQueue<T>(string queue, ServiceOperationDescriptor<T, Empty> operation, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true)
        => Add(queue, operation, configureChannel, requeueOnFailure);

    private RabbitMQBuilder Add(string queueName, ServiceOperationDescriptor operation, Func<IChannel, CancellationToken, Task>? configureChannel, bool requeueOnFailure)
    {
        lock (_gate)
        {
            EnsureMutable();
            ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
            var consumerId = "rabbitmq:queue:" + queueName;
            ArgumentNullException.ThrowIfNull(operation);
            if (operation.ResponseType != typeof(Empty) || !ServiceOperationInspector.CanSubmit(operation))
            {
                throw new InvalidOperationException($"Operation '{operation.Id}' must return Empty without declared failures for asynchronous consumption.");
            }
            if (_services.Any(item => item.ServiceType == typeof(IInboxHandler) && item.IsKeyedService && Equals(item.ServiceKey, consumerId)))
            {
                throw new InvalidOperationException($"Inbox endpoint '{consumerId}' has already been mapped.");
            }
            _endpoints.Add(new(queueName, consumerId, operation, configureChannel, requeueOnFailure));
            _services.AddInbox();
            _services.AddKeyedScoped<IInboxHandler>(consumerId, (provider, _) => new OperationInboxHandler(operation,
                provider.GetRequiredService<IObjectSerializer>(), provider.GetRequiredService<IOperationExecutor>()));
            return this;
        }
    }

    private void EnsureMutable()
    {
        if (_frozen || _services.IsReadOnly)
        {
            throw new InvalidOperationException("Configure RabbitMQ before building or starting the host.");
        }
    }
}

/// <summary>One queue consumer and its native channel settings.</summary>
public sealed record RabbitMQEndpoint(string QueueName, string ConsumerId, ServiceOperationDescriptor Operation,
    Func<IChannel, CancellationToken, Task>? ConfigureChannel = null, bool RequeueOnFailure = true);
