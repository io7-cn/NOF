using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Owns one lazy native connection; native RabbitMQ handles recovery and channel configuration.</summary>
public sealed class RabbitMQConnection(IConnectionFactory factory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task<IConnection>? _connection;
    private Task? _disposal;
    private bool _disposed;

    /// <summary>Gets the shared connection. Dispose caller-created channels, not this connection.</summary>
    /// <remarks>Caller cancellation cancels only its wait. A failed initial connection can be retried;
    /// established connections retain native automatic recovery.</remarks>
    public Task<IConnection> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is null || _connection.IsFaulted || _connection.IsCanceled)
            {
                _connection = factory.CreateConnectionAsync(_lifetime.Token);
            }
            return _connection.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return new(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _lifetime.Cancel();
            if (_connection is not null)
            {
                IConnection connection;
                try
                {
                    connection = await _connection.ConfigureAwait(false);
                }
                catch when (_connection.IsFaulted || _connection.IsCanceled)
                {
                    return;
                }
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
