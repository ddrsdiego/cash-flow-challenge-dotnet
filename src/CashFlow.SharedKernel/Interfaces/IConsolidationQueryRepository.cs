namespace CashFlow.SharedKernel.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CSharpFunctionalExtensions;

/// <summary>
/// Read-only repository interface for querying daily consolidations from MongoDB.
/// Used by the Consolidation API for cache operations and by the Worker for batch queries.
/// 
/// This interface is segregated from write operations to comply with ISP (Interface Segregation Principle).
/// </summary>
public interface IConsolidationQueryRepository
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
}
