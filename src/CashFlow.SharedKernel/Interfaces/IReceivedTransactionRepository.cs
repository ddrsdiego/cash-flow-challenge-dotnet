using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository for managing received transactions (batch ingestion).
/// </summary>
public interface IReceivedTransactionRepository
{
    /// <summary>
    /// Bulk insert transactions received in a batch.
    /// </summary>
    /// <param name="transactions">Collection of transactions to insert</param>
    /// <param name="session">MongoDB session for transactional writes (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BulkInsertAsync(
        IEnumerable<ReceivedTransaction> transactions,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all transactions for a specific batch.
    /// </summary>
    /// <param name="batchId">Batch identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of received transactions for the batch</returns>
    Task<IReadOnlyCollection<ReceivedTransaction>> GetByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delete transactions belonging to a specific batch.
    /// Used after batch has been processed and consolidated.
    /// </summary>
    /// <param name="batchId">Batch identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken);
}
