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

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Repository for managing raw transaction requests in the batch processing pipeline.
/// Full implementation in Worker: FindPendingAsync, MarkAsDispatchedAsync, MarkAsProcessedAsync, GetByBatchIdAsync.
/// </summary>
public class RawRequestRepository : IRawRequestRepository
{
    private readonly ITransactionsWorkerMongoDbContext _context;

    public RawRequestRepository(ITransactionsWorkerMongoDbContext context)
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

    public async Task<IReadOnlyCollection<RawRequest>> FindPendingAsync(int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<RawRequest>.Filter.Eq(r => r.Status, RawRequestStatus.Pending);
        
        var requests = await _context.RawRequests
            .Find(filter)
            .SortBy(r => r.CreatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return requests.AsReadOnly();
    }

<<<<<<< HEAD
    public async Task MarkAsDispatchedAsync(IEnumerable<string> requestIds, string batchId,
=======
    public async Task MarkAsDispatchedAsync(
        IEnumerable<string> requestIds,
        string batchId,
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
        CancellationToken cancellationToken)
    {
        var ids = requestIds.ToList();
        
        if (ids.Count == 0)
            return;

        var filter = Builders<RawRequest>.Filter.In(r => r.Id, ids);
        var update = Builders<RawRequest>.Update
            .Set(r => r.Status, RawRequestStatus.Dispatched)
            .Set(r => r.DispatchedAt, DateTime.UtcNow)
            .Set(r => r.BatchId, batchId);

        var options = new UpdateOptions { IsUpsert = false };

        await _context.RawRequests.UpdateManyAsync(filter, update, options, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RawRequest>> FindOrphanedDispatchedAsync(
        int minutesThreshold,
        CancellationToken cancellationToken)
    {
        var thresholdTime = DateTime.UtcNow.AddMinutes(-minutesThreshold);

        var filter = Builders<RawRequest>.Filter.And(
            Builders<RawRequest>.Filter.Eq(r => r.Status, RawRequestStatus.Dispatched),
            Builders<RawRequest>.Filter.Lt(r => r.DispatchedAt, thresholdTime)
        );

        var orphaned = await _context.RawRequests
            .Find(filter)
            .ToListAsync(cancellationToken);

<<<<<<< HEAD
=======
        // Reset status back to Pending for retry
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
        if (orphaned.Count > 0)
        {
            var orphanedIds = orphaned.Select(r => r.Id).ToList();
            var orphanFilter = Builders<RawRequest>.Filter.In(r => r.Id, orphanedIds);
            var resetUpdate = Builders<RawRequest>.Update
                .Set(r => r.Status, RawRequestStatus.Pending)
<<<<<<< HEAD
                .Set(r => r.DispatchedAt, default);
=======
                .Set(r => r.DispatchedAt, default(DateTime));
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12

            await _context.RawRequests.UpdateManyAsync(orphanFilter, resetUpdate, cancellationToken: cancellationToken);
        }

        return orphaned.AsReadOnly();
    }

    public async Task MarkAsProcessedAsync(
        IEnumerable<string> requestIds,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var ids = requestIds.ToList();
        
        if (ids.Count == 0)
            return;

        var filter = Builders<RawRequest>.Filter.In(r => r.Id, ids);
        var update = Builders<RawRequest>.Update
            .Set(r => r.Status, RawRequestStatus.Processed)
            .Set(r => r.ProcessedAt, DateTime.UtcNow);

        var options = new UpdateOptions { IsUpsert = false };

        if (session != null)
            await _context.RawRequests.UpdateManyAsync(session, filter, update, options, cancellationToken);
        else
            await _context.RawRequests.UpdateManyAsync(filter, update, options, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RawRequest>> GetByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<RawRequest>.Filter.Eq(r => r.BatchId, batchId);
        
        var requests = await _context.RawRequests
            .Find(filter)
            .ToListAsync(cancellationToken);

        return requests.AsReadOnly();
    }
}
