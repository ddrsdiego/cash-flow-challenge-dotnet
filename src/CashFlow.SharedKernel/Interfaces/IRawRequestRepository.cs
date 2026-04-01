using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository for managing raw transaction requests in the fast ingestion pipeline.
/// Handles persistence, status tracking, and batch processing coordination.
/// Inherits ingestion operations from IRawRequestIngestionRepository for segregated interfaces.
/// </summary>
public interface IRawRequestRepository : IRawRequestIngestionRepository
{
    /// <summary>
    /// Find pending raw requests for batch processing.
    /// Ordered by creation date (FIFO).
    /// </summary>
    /// <param name="limit">Maximum number of requests to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of pending RawRequests</returns>
    Task<IReadOnlyCollection<RawRequest>> FindPendingAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically mark raw requests as dispatched (status change: Pending → Dispatched).
    /// Used as a claim operation to prevent race conditions in multi-instance scenario.
    /// </summary>
    /// <param name="requestIds">IDs of requests to mark as dispatched</param>
    /// <param name="batchId">Batch ID to assign</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task MarkAsDispatchedAsync(
        IEnumerable<string> requestIds,
        string batchId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Find requests dispatched for more than the specified minutes (orphaned requests).
    /// Used by sweep operation to recover from failed workers.
    /// </summary>
    /// <param name="minutesThreshold">Requests dispatched more than this many minutes ago</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of orphaned RawRequests (status reset to Pending)</returns>
    Task<IReadOnlyCollection<RawRequest>> FindOrphanedDispatchedAsync(
        int minutesThreshold,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically mark raw requests as processed (status change: Dispatched → Processed).
    /// </summary>
    /// <param name="requestIds">IDs of requests to mark as processed</param>
    /// <param name="session">MongoDB session for transactional writes (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task MarkAsProcessedAsync(
        IEnumerable<string> requestIds,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all raw requests in a batch.
    /// </summary>
    /// <param name="batchId">Batch identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of RawRequests in the batch</returns>
    Task<IReadOnlyCollection<RawRequest>> GetByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken);
}
