using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CSharpFunctionalExtensions;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Cache abstraction for DailyConsolidation read operations.
/// Backed by Redis. Provides Cache-First pattern for the Consolidation API.
/// </summary>
public interface IConsolidationCache
{
    /// <summary>
    /// Retrieves a cached daily consolidation for the specified date.
    /// Returns Maybe.None on cache miss or if the cache is unavailable.
    /// </summary>
    Task<Maybe<DailyConsolidation>> GetAsync(
        DateTime date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a daily consolidation in the cache with the specified TTL.
    /// Fails silently if the cache is unavailable.
    /// </summary>
    Task SetAsync(
        DateTime date,
        DailyConsolidation consolidation,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached entry for the specified date.
    /// Called by the Consolidation Worker after recalculating the daily balance.
    /// Fails silently if the cache is unavailable.
    /// </summary>
    Task InvalidateAsync(
        DateTime date,
        CancellationToken cancellationToken = default);
}
