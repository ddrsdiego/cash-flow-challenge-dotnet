using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CSharpFunctionalExtensions;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository for distributed locking via MongoDB.
/// Ensures only one Worker instance performs batch polling and dispatching at a time.
/// </summary>
public interface IDistributedLockRepository
{
    /// <summary>
    /// Attempt to acquire or renew a distributed lock.
    /// Uses atomic findOneAndUpdate operation for consistency.
    /// </summary>
    /// <param name="lockId">Lock identifier (e.g., "transaction-batcher")</param>
    /// <param name="instanceId">Current instance ID (pod name, hostname, etc.)</param>
    /// <param name="ttlSeconds">Lock TTL in seconds (default: 30)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Maybe with DistributedLock if acquisition succeeded; None if lock is held by another instance</returns>
    Task<Maybe<DistributedLock>> TryAcquireAsync(
        string lockId,
        string instanceId,
        int ttlSeconds = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a lock is currently held by the calling instance.
    /// </summary>
    /// <param name="lockId">Lock identifier</param>
    /// <param name="instanceId">Expected instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if lock is held by the specified instance</returns>
    Task<bool> IsHeldByAsync(
        string lockId,
        string instanceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Release a lock held by this instance.
    /// </summary>
    /// <param name="lockId">Lock identifier</param>
    /// <param name="instanceId">Instance ID holding the lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReleaseAsync(
        string lockId,
        string instanceId,
        CancellationToken cancellationToken);
}
