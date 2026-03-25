using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.DTOs.Responses;
using CSharpFunctionalExtensions;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Cache abstraction for DailyConsolidation read operations.
/// Stores pre-computed DailyConsolidationResponse (DTO) for cache hits.
/// Backed by in-process MemoryCache or distributed cache.
/// Provides Cache-First pattern for the Consolidation API.
/// Uses ValueTask for zero-allocation on synchronous cache hits.
/// </summary>
public interface IConsolidationCache
{
    /// <summary>
    /// Retrieves a cached daily consolidation response for the specified key.
    /// Returns Maybe.None on cache miss or if the cache is unavailable.
    /// Synchronous operations use ValueTask for zero-allocation performance.
    /// </summary>
    ValueTask<Maybe<DailyConsolidationResponse>> GetAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a pre-computed daily consolidation response in the cache with the specified TTL.
    /// Fails silently if the cache is unavailable.
    /// </summary>
    ValueTask SetAsync(
        ConsolidationKey key,
        DailyConsolidationResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached entry for the specified key.
    /// Called after the consolidation is updated or invalidated.
    /// Fails silently if the cache is unavailable.
    /// </summary>
    ValueTask InvalidateAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default);
}
