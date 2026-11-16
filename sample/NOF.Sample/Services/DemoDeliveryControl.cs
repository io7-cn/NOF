namespace NOF.Sample.Services;

/// <summary>Application-owned local demonstration controls; production transport behavior stays in the integration.</summary>
public sealed class DemoDeliveryControl
{
    private readonly Lock _gate = new();
    private TaskCompletionSource? _resume;
    private int _failNext;

    public bool Paused
    {
        get
        {
            lock (_gate)
            {
                return _resume is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? resume;
        lock (_gate)
        {
            resume = _resume;
            _resume = null;
        }
        resume?.TrySetResult();
    }

    public void FailNext() => Interlocked.Exchange(ref _failNext, 1);

    public async Task BeforeDeliveryAsync(CancellationToken cancellationToken)
    {
        Task? wait;
        lock (_gate)
        {
            wait = _resume?.Task;
        }
        if (wait is not null)
        {
            await wait.WaitAsync(cancellationToken);
        }
        if (Interlocked.Exchange(ref _failNext, 0) != 0)
        {
            throw new InvalidOperationException("Demonstration: the first publish attempt failed before contacting RabbitMQ.");
        }
    }
}
