using Microsoft.Extensions.Caching.Distributed;
using NOF.Contract;

namespace NOF.Application;

/// <summary>
/// Represents a distributed lock that can be released asynchronously.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>
    /// Gets the lock key.
    /// </summary>
    string Key { get; }
    /// <summary>
    /// Gets a value indicating whether the lock is currently acquired.
    /// </summary>
    bool IsAcquired { get; }

    /// <summary>
    /// Releases the distributed lock.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lock was released; otherwise, false.</returns>
    ValueTask<bool> ReleaseAsync(CancellationToken cancellationToken = default);
}
