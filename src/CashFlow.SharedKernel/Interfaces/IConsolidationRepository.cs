using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository interface for the DailyConsolidation aggregate.
/// Implementations are in the CashFlow.Consolidation infrastructure layer.
/// </summary>
public interface IConsolidationRepository
{
    /// <summary>
    /// Finds a daily consolidation by its composite key (userId + date).
    /// Used by the Consolidation API for cache miss reads.
    /// O(1) lookup via unique index on consolidationKey field.
    /// </summary>
    /// <param name="key">ConsolidationKey (composite of userId + date)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Maybe with DailyBalances if found, None if not found</returns>
    Task<Maybe<DailyBalances>> FindByKeyAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds daily consolidations for multiple ConsolidationKey objects in a single query.
    /// Used during batch processing to identify existing consolidations that need updating.
    /// Significantly more efficient than calling Find multiple times (avoids N+1 anti-pattern).
    /// Uses O(1) lookup via unique index on consolidationKey field.
    /// </summary>
    /// <param name="keys">Enumerable of ConsolidationKey objects to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of DailyBalances documents; documents for non-existent keys are simply omitted</returns>
    Task<IReadOnlyCollection<DailyBalances>> FindDailyConsolidationsByKeysAsync(
        IEnumerable<ConsolidationKey> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies calculated daily consolidations to persistent storage in a single atomic operation.
    /// Used after batch processing to commit consolidation results.
    /// Significantly more efficient than applying consolidations one at a time (avoids N+1 anti-pattern).
    /// All operations are part of the same batch write, improving throughput.
    /// </summary>
    /// <param name="consolidations">Enumerable of DailyConsolidation documents to apply</param>
    /// <param name="session">MongoDB session for transactional writes (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ApplyDailyConsolidationsAsync(
        IEnumerable<DailyBalances> consolidations,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);
}
