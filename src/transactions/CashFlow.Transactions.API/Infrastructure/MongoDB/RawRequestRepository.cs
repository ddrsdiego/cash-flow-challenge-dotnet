using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Transactions.API.Infrastructure.MongoDB;

/// <summary>
/// Repository for managing raw transaction requests in fast ingestion.
/// Minimal implementation in API: only ingestion operations.
/// Implements IRawRequestIngestionRepository (ingestion-only, segregated interface).
/// Full batch processing implementation is in Transactions.Worker.
/// </summary>
public class RawRequestRepository : IRawRequestIngestionRepository
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
}
