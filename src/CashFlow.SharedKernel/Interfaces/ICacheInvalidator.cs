using System;
using System.Threading;
using System.Threading.Tasks;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Abstraction for cache invalidation operations.
/// Used by the Consolidation Worker after updating the daily balance
/// to ensure the next read fetches fresh data from MongoDB.
/// Fire-and-forget semantics: failures are swallowed to avoid aborting the main flow.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Invalidates the cache entry for the specified date.
    /// Implementations should not throw — log and swallow on failure.
    /// </summary>
    Task InvalidateAsync(DateTime date, CancellationToken cancellationToken = default);
}
