using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Transactions.API.Infrastructure.MongoDB;

/// <summary>
/// Repository for managing raw transaction requests in fast ingestion.
/// Minimal implementation in API: only GetByIdempotencyKeyAsync and InsertAsync.
/// Full implementation is in Transactions.Worker.
/// </summary>
public class RawRequestRepository : IRawRequestRepository
{
    private readonly MongoDbContext _context;

    public RawRequestRepository(MongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Maybe<RawRequest>> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var filter = Builders<RawRequest>.Filter.Eq(r => r.IdempotencyKey, idempotencyKey);
        var request = await _context.RawRequests
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return request == null
            ? Maybe<RawRequest>.None
            : Maybe<RawRequest>.From(request);
    }

    public async Task InsertAsync(
        RawRequest request,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        if (session != null)
            await _context.RawRequests.InsertOneAsync(session, request, cancellationToken: cancellationToken);
        else
            await _context.RawRequests.InsertOneAsync(request, cancellationToken: cancellationToken);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Not implemented in API — only in Worker
    // ═════════════════════════════════════════════════════════════════════════════

    public Task<IReadOnlyCollection<RawRequest>> FindPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Use Transactions.Worker.Infrastructure.MongoDB.RawRequestRepository");
    }

    public Task MarkAsDispatchedAsync(
        IEnumerable<string> requestIds,
        string batchId,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Use Transactions.Worker.Infrastructure.MongoDB.RawRequestRepository");
    }

    public Task<IReadOnlyCollection<RawRequest>> FindOrphanedDispatchedAsync(
        int minutesThreshold,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Use Transactions.Worker.Infrastructure.MongoDB.RawRequestRepository");
    }

    public Task MarkAsProcessedAsync(
        IEnumerable<string> requestIds,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Use Transactions.Worker.Infrastructure.MongoDB.RawRequestRepository");
    }

    public Task<IReadOnlyCollection<RawRequest>> GetByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Use Transactions.Worker.Infrastructure.MongoDB.RawRequestRepository");
    }
}
