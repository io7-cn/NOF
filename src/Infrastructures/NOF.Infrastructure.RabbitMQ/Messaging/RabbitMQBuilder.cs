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
    public RabbitMQBuilder MapQueue<TOperation>(string queue, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true) where TOperation : OperationHandler
        => Add(queue, _services.GetOrAddSingleton<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)), configureChannel, requeueOnFailure);

    /// <summary>Maps an explicitly typed one-way operation to a queue.</summary>
    public RabbitMQBuilder MapQueue<T>(string queue, OperationDescriptor<T> operation, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true)
        => Add(queue, operation, configureChannel, requeueOnFailure);

    private RabbitMQBuilder Add(string queueName, OperationDescriptor operation, Func<IChannel, CancellationToken, Task>? configureChannel, bool requeueOnFailure)
    {
        lock (_gate)
        {
            EnsureMutable();
            ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
            ArgumentNullException.ThrowIfNull(operation);
            if (!OperationInspector.CanSubmit(operation))
            {
                throw new ArgumentException("RabbitMQ endpoints require a void operation without business failure cases.", nameof(operation));
            }
            if (_endpoints.Any(endpoint => endpoint.QueueName == queueName))
            {
                throw new InvalidOperationException($"RabbitMQ queue '{queueName}' has already been mapped.");
            }
            _endpoints.Add(new(queueName, operation, configureChannel, requeueOnFailure));
            _services.AddNOFJson();
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
