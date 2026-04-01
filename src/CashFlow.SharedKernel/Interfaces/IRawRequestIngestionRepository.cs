using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Ingestion-only repository interface for raw transaction requests.
/// Segregated from batch processing operations to follow Interface Segregation Principle.
/// Used by the Transactions.API for fast ingestion only.
/// </summary>
public interface IRawRequestIngestionRepository
{
    /// <summary>
    /// Get a raw request by its unique idempotency key.
    /// Used for request deduplication.
    /// </summary>
    /// <param name="idempotencyKey">Client-provided idempotency key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Maybe with RawRequest if found; None if not found</returns>
    Task<Maybe<RawRequest>> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persist a new raw request.
    /// </summary>
    /// <param name="request">RawRequest to persist</param>
    /// <param name="session">MongoDB session for transactional writes (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InsertAsync(
        RawRequest request,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);
}
