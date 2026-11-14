using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

public class RabbitMQConnectionManager : IDisposable, IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly RabbitMQOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMQConnectionManager(IOptions<RabbitMQOptions> options)
    {
        _options = options.Value;
        _connectionFactory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };
    }

    /// <summary>Gets the shared native connection; callers must not dispose it.</summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMQConnectionManager));
        }

        if (_connection != null && _connection.IsOpen)
        {
            return _connection;
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>Creates a caller-owned native channel with the supplied RabbitMQ channel options.</summary>
    public async Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(options, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connectionSemaphore.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _connection?.Dispose();
            _connection = null;
            _disposed = true;
        }
        finally
        {
            _connectionSemaphore.Release();
            _connectionSemaphore.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _connectionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _connection?.Dispose();
            _connection = null;
            _disposed = true;
        }
        finally
        {
            _connectionSemaphore.Release();
            _connectionSemaphore.Dispose();
        }
    }
}
